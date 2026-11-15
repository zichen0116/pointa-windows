using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Agreement.Srp;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Pointa.Core;
using Pointa.Server;

internal static class Program
{
    private static int checks;
    private static readonly string directory = Path.Combine(Path.GetTempPath(), "Pointa-selftest-" + Guid.NewGuid().ToString("N"));

    private static async Task Main()
    {
        try
        {
            UnitChecks();
            await IntegrationChecks();
            Console.WriteLine($"PASS: {checks} assertions (protocol, DPAPI persistence, pinned TLS/WSS, QR, SRP, replay, revocation, timeout, UDP discovery).");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }

    private static void UnitChecks()
    {
        Check(new LaserEffect().IsValid, "default effect valid");
        Check(!new LaserEffect(Size: 41).IsValid && !new LaserEffect(Color: "red").IsValid && !new LaserEffect(TrailMs: 1201).IsValid, "effect boundaries");
        var guard = new SessionGuard();
        const long now = 10_000;
        Check(guard.Validate(1, 1, now, now) is null, "first sequence");
        Check(guard.Validate(1, 1, now, now) == "invalid_sequence", "duplicate sequence");
        Check(guard.Validate(1, 0, now, now) == "invalid_sequence", "nonpositive sequence");
        Check(guard.Validate(2, 2, now, now) == "unsupported_version", "version rejection");
        Check(guard.Validate(1, 2, now - 2001, now) == "stale_message", "stale timestamp");
        Check(guard.Validate(1, 2, now + 1001, now) == "stale_message", "future timestamp");
        for (var i = 2; i <= 240; i++) Check(guard.Validate(1, i, now, now) is null, "accepted rate " + i);
        Check(guard.Validate(1, 241, now, now) == "rate_limited", "rate bound");
        Check(guard.Validate(1, 242, now + 1000, now + 1000) is null, "rate recovery");
        try { _ = new HostService(new(directory, [IPAddress.Any]), (_, _) => Task.FromResult(true)); Check(false, "wildcard denied"); }
        catch (ArgumentException) { Check(true, "wildcard denied"); }
        try { _ = new HostService(new(directory, [IPAddress.Parse("8.8.8.8")]), (_, _) => Task.FromResult(true)); Check(false, "public address denied"); }
        catch (ArgumentException) { Check(true, "public address denied"); }
    }

    private static async Task IntegrationChecks()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var discoveryPortProbe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var discoveryPort = ((IPEndPoint)discoveryPortProbe.Client.LocalEndPoint!).Port;
        discoveryPortProbe.Close();
        var options = new HostOptions(directory, [IPAddress.Loopback], "Self test", port, discoveryPort);
        var allow = true;
        var approvals = 0;
        await using var host = new HostService(options, (_, _) => { approvals++; return Task.FromResult(allow); });
        Check(host.Display is null, "unknown display is not fabricated");
        try { host.SetDisplay(new(0, 1080)); Check(false, "invalid display denied"); }
        catch (ArgumentException) { Check(host.Display is null, "invalid display denied"); }
        host.SetDisplay(new(2560, 1440));
        var pointer = new PointerState(.5, .5, false);
        var slides = 0;
        host.PointerChanged += value => pointer = value;
        host.SlideRequested = (_, cancellation) =>
        {
            // Models a UI callback that reads host state on another thread before returning.
            var connected = Task.Run(() => host.ConnectedDevice).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Check(connected is not null && !cancellation.IsCancellationRequested, "slide callback runs outside state lock");
            slides++;
            return new(true);
        };
        await host.StartAsync();
        var initial = host.Pairing;
        Check(initial.Code.Length == 6 && initial.Code.All(char.IsAsciiDigit), "six digit code");
        Check(initial.QrToken.Length == 43 && initial.CertificateSha256.Length == 64, "token and pin sizes");
        Check(initial.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(175) && initial.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(180), "three-minute pairing lifetime");
        Check(initial.QrPayload.StartsWith("pointa://pair?v=1&") && initial.QrPayload.Contains(initial.QrToken), "QR URI");
        using (var udp = new UdpClient(AddressFamily.InterNetwork))
        {
            await udp.SendAsync(Encoding.ASCII.GetBytes("POINTA_DISCOVER_V1\n"), new IPEndPoint(IPAddress.Loopback, discoveryPort));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await udp.ReceiveAsync(timeout.Token);
            var text = Encoding.UTF8.GetString(response.Buffer);
            Check(text.Contains(initial.ServerId) && !text.Contains(initial.QrToken) && !text.Contains(initial.Code), "UDP discovery contains no secrets");
        }

        using (var badPin = new ClientWebSocket())
        {
            badPin.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())) == new string('0', 64);
            try { await badPin.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/ws"), CancellationToken.None); Check(false, "pin mismatch denied"); }
            catch (WebSocketException) { Check(true, "pin mismatch denied"); }
        }
        using (var browser = new ClientWebSocket())
        {
            browser.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).Equals(initial.CertificateSha256, StringComparison.OrdinalIgnoreCase);
            browser.Options.SetRequestHeader("Origin", "https://example.com");
            try { await browser.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/ws"), CancellationToken.None); Check(false, "browser origin denied"); }
            catch (WebSocketException) { Check(true, "browser origin denied"); }
        }

        string deviceId, credential, firstAction = Guid.NewGuid().ToString();
        await using (var client = await Client.Connect(host.Pairing))
        {
            await client.Send(new { v = 1, type = "pair.qr", name = "Android test", pairingId = initial.PairingId, token = initial.QrToken });
            Check((await client.Receive()).GetProperty("type").GetString() == "pair.pending", "PC approval stage");
            var approved = await client.Receive();
            Check(approved.GetProperty("type").GetString() == "pair.approved" && approvals == 1, "QR PC approved");
            deviceId = approved.GetProperty("deviceId").GetString()!;
            credential = approved.GetProperty("credential").GetString()!;
            var ready = await client.Receive();
            Check(ready.GetProperty("type").GetString() == "session.ready", "QR controller ready");
            Check(ready.GetProperty("display").GetProperty("width").GetInt32() == 2560 &&
                ready.GetProperty("display").GetProperty("height").GetInt32() == 1440, "selected display in ready");
            host.SetDisplay(new(1080, 1920));
            await client.Control("heartbeat");
            var heartbeat = await client.Receive();
            Check(heartbeat.GetProperty("display").GetProperty("width").GetInt32() == 1080 &&
                heartbeat.GetProperty("display").GetProperty("height").GetInt32() == 1920, "hotplug display in heartbeat");
            host.SetDisplay(null);
            await client.Control("heartbeat");
            Check((await client.Receive()).GetProperty("display").ValueKind == JsonValueKind.Null, "missing display stays null on wire");
            Check(host.Devices.Count == 1 && host.ConnectedDevice?.Id == deviceId, "device recorded");
            Check(host.Pairing.QrToken != initial.QrToken, "one time pairing rotated");
            await client.Control("pointer.move", new() { ["x"] = .25, ["y"] = .75, ["active"] = true });
            await WaitFor(() => pointer.Active && pointer.X == .25 && pointer.Y == .75);
            Check(pointer.Active, "pointer accepted without mouse injection");
            await client.Control("pointer.hide");
            await WaitFor(() => !pointer.Active);
            Check(!pointer.Active, "explicit hide");
            await client.Control("slide.next", new() { ["id"] = firstAction });
            Check((await client.Receive()).GetProperty("success").GetBoolean() && slides == 1, "single slide execution");
            await client.Control("slide.next", new() { ["id"] = firstAction.ToUpperInvariant() });
            Check((await client.Receive()).GetProperty("error").GetString() == "duplicate_action" && slides == 1, "canonical UUID deduplication");
            await client.Control("effect.update", new() { ["size"] = 20, ["trailMs"] = 800, ["color"] = "#00AAFF", ["smoothing"] = "high" });
            Check((await client.Receive()).GetProperty("type").GetString() == "effect.applied" && host.Effect.Size == 20, "effect validation and persist");
            await using var second = await Client.Connect(host.Pairing);
            await second.Send(new { v = 1, type = "auth.resume", deviceId, credential });
            Check((await second.Receive()).GetProperty("code").GetString() == "controller_busy", "single controller");
        }
        await WaitFor(() => host.ConnectedDevice is null);
        Check(!pointer.Active, "disconnect safe hide");
        var protectedFile = File.ReadAllBytes(Path.Combine(directory, "trust.dat"));
        Check(!Encoding.UTF8.GetString(protectedFile).Contains(credential), "raw credential absent from protected file");

        await using (var client = await Client.Resume(host.Pairing, deviceId, credential))
        {
            Check(!pointer.Active, "resume starts hidden");
            await client.Control("slide.next", new() { ["id"] = firstAction });
            Check((await client.Receive()).GetProperty("error").GetString() == "duplicate_action" && slides == 1, "deduplication survives reconnect");
            await client.Control("pointer.move", new() { ["x"] = 1.1, ["y"] = .5, ["active"] = true });
            Check((await client.Receive()).GetProperty("code").GetString() == "invalid_pointer", "out of range rejected");
        }
        await WaitFor(() => host.ConnectedDevice is null);
        await using (var client = await Client.Resume(host.Pairing, deviceId, credential))
        {
            await client.Send(new { v = 1, type = "slide.next", seq = 1, ts = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds(), id = Guid.NewGuid() });
            Check((await client.Receive()).GetProperty("code").GetString() == "stale_message" && slides == 1, "expired slide never executes");
        }
        await WaitFor(() => host.ConnectedDevice is null);
        await using (var client = await Client.Resume(host.Pairing, deviceId, credential))
        {
            await client.Control("heartbeat"); await client.Receive();
            await client.Send(new { v = 1, type = "heartbeat", seq = 1, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            Check((await client.Receive()).GetProperty("code").GetString() == "invalid_sequence", "wire replay sequence rejected");
        }
        await WaitFor(() => host.ConnectedDevice is null);
        await using (var client = await Client.Resume(host.Pairing, deviceId, credential))
        {
            await client.SendRaw(new string('a', 4097));
            Check((await client.Receive()).GetProperty("code").GetString() == "message_too_large", "frame size limit");
        }
        await WaitFor(() => host.ConnectedDevice is null);
        await using (var client = await Client.Resume(host.Pairing, deviceId, credential))
        {
            await client.SendRaw("{\"v\":1,\"v\":1,\"type\":\"heartbeat\",\"seq\":1,\"ts\":0}");
            Check((await client.Receive()).GetProperty("code").GetString() == "duplicate_property", "duplicate JSON fields rejected");
        }
        await WaitFor(() => host.ConnectedDevice is null);

        host.RefreshPairing();
        await using (var client = await Client.Connect(host.Pairing))
        {
            await client.Send(new { v = 1, type = "pair.qr", name = "replay", pairingId = initial.PairingId, token = initial.QrToken });
            Check((await client.Receive()).GetProperty("code").GetString() == "pairing_expired", "old QR invalid after refresh");
        }
        allow = false;
        var denied = host.Pairing;
        await using (var client = await Client.Connect(denied))
        {
            await client.Send(new { v = 1, type = "pair.qr", name = "denied", pairingId = denied.PairingId, token = denied.QrToken });
            await client.Receive();
            Check((await client.Receive()).GetProperty("code").GetString() == "pairing_denied" && host.Devices.Count == 1, "PC denial grants no trust");
        }
        allow = true;
        host.RefreshPairing();
        await SrpChecks(host);
        await WaitFor(() => host.ConnectedDevice is null);
        await using (var client = await Client.Resume(host.Pairing, deviceId, credential))
        {
            await client.Control("pointer.move", new() { ["x"] = .6, ["y"] = .5, ["active"] = true });
            await WaitFor(() => pointer.Active);
            await WaitFor(() => host.ConnectedDevice is null, 6_000);
            Check(!pointer.Active, "heartbeat timeout hides and disconnects");
        }
        await using (var client = await Client.Resume(host.Pairing, deviceId, credential))
        {
            using var released = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.SlideRequested = (_, cancellation) =>
            {
                entered.TrySetResult();
                released.Wait(TimeSpan.FromSeconds(2));
                finished.TrySetResult(cancellation.IsCancellationRequested);
                return new(!cancellation.IsCancellationRequested, "cancelled");
            };
            await client.Control("slide.next", new() { ["id"] = Guid.NewGuid().ToString() });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            host.RevokeDevice(deviceId);
            released.Set();
            Check(await finished.Task.WaitAsync(TimeSpan.FromSeconds(2)), "revocation cancels a queued presentation action");
            Check(host.ConnectedDevice is null && !host.Devices.Any(x => x.Id == deviceId) && !pointer.Active, "revocation immediately interrupts");
        }
        await using (var client = await Client.Connect(host.Pairing))
        {
            await client.Send(new { v = 1, type = "auth.resume", deviceId, credential });
            Check((await client.Receive()).GetProperty("code").GetString() == "invalid_credential", "revoked credential denied");
        }
        await host.StopAsync();
        await using (var restored = new HostService(options, (_, _) => Task.FromResult(true)))
        {
            Check(restored.Pairing.CertificateSha256 == initial.CertificateSha256 && restored.Pairing.ServerId == initial.ServerId, "persistent cert and server identity");
            Check(restored.Devices.Count == 1 && restored.Effect.Size == 20, "protected device list and effect recovered");
            restored.ClearDevices();
            Check(restored.Devices.Count == 0, "clear trust");
        }
        var damaged = Path.Combine(directory, "damaged"); Directory.CreateDirectory(damaged);
        var damagedPath = Path.Combine(damaged, "trust.dat"); File.WriteAllText(damagedPath, "do not overwrite");
        try { await using var invalid = new HostService(options with { DataDirectory = damaged }, (_, _) => Task.FromResult(true)); Check(false, "damaged trust refused"); }
        catch (InvalidDataException) { Check(File.ReadAllText(damagedPath) == "do not overwrite", "damaged trust preserved"); }
    }

    private static async Task SrpChecks(HostService host)
    {
        foreach (var wrongBinding in new[] { true, false })
        {
            var pairing = host.Pairing;
            await using var wire = await Client.Connect(pairing);
            await wire.Send(new { v = 1, type = "pair.srp.begin", name = "SRP phone", pairingId = pairing.PairingId });
            var challenge = await wire.Receive();
            Check(challenge.GetProperty("group").GetString() == "rfc5054-3072", "SRP group");
            var client = new Srp6Client(); client.Init(Srp6StandardGroups.rfc5054_3072, new Sha256Digest(), new SecureRandom());
            var binding = wrongBinding ? new string('0', 64) : pairing.CertificateSha256;
            var identity = Encoding.UTF8.GetBytes($"pointa-srp-v1\n{pairing.ServerId}\n{pairing.PairingId}\n{binding}");
            var a = client.GenerateClientCredentials(Convert.FromHexString(challenge.GetProperty("salt").GetString()!), identity, Encoding.ASCII.GetBytes(pairing.Code));
            client.CalculateSecret(new BigInteger(challenge.GetProperty("b").GetString(), 16));
            await wire.Send(new { v = 1, type = "pair.srp.proof", a = Hex(a, 384), m1 = Hex(client.CalculateClientEvidenceMessage(), 32) });
            var proof = await wire.Receive();
            if (wrongBinding)
            {
                Check(proof.GetProperty("code").GetString() == "invalid_proof", "SRP relay certificate mismatch rejected");
                continue;
            }
            Check(client.VerifyServerEvidenceMessage(new BigInteger(proof.GetProperty("m2").GetString(), 16)), "SRP mutual proof");
            var key = Convert.FromHexString(Hex(client.CalculateSessionKey(), 32));
            var confirmation = Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("pointa-srp-confirm-v1\n" + pairing.PairingId))).ToLowerInvariant();
            await wire.Send(new { v = 1, type = "pair.srp.confirm", confirmation });
            Check((await wire.Receive()).GetProperty("type").GetString() == "pair.pending", "SRP PC approval required");
            Check((await wire.Receive()).GetProperty("type").GetString() == "pair.approved", "SRP credential after mutual proof");
            Check((await wire.Receive()).GetProperty("type").GetString() == "session.ready", "SRP controller ready");
        }
    }

    private static string Hex(BigInteger value, int bytes) => value.ToString(16).PadLeft(bytes * 2, '0');
    private static async Task WaitFor(Func<bool> predicate, int milliseconds = 2000)
    {
        var start = Environment.TickCount64;
        while (!predicate()) { if (Environment.TickCount64 - start > milliseconds) throw new TimeoutException("Expected state did not arrive."); await Task.Delay(10); }
    }

    private sealed class Client : IAsyncDisposable
    {
        private readonly ClientWebSocket socket = new();
        private long sequence;
        public static async Task<Client> Connect(PairingInfo pairing)
        {
            var client = new Client();
            client.socket.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null &&
                Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())).Equals(pairing.CertificateSha256, StringComparison.OrdinalIgnoreCase);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.socket.ConnectAsync(new Uri($"wss://127.0.0.1:{pairing.Port}/ws"), timeout.Token);
            Check((await client.Receive()).GetProperty("type").GetString() == "hello", "WSS hello");
            return client;
        }
        public static async Task<Client> Resume(PairingInfo pairing, string deviceId, string credential)
        {
            var client = await Connect(pairing);
            await client.Send(new { v = 1, type = "auth.resume", deviceId, credential });
            Check((await client.Receive()).GetProperty("type").GetString() == "session.ready", "credential resume");
            return client;
        }
        public Task Control(string type, Dictionary<string, object>? fields = null)
        {
            fields ??= []; fields["v"] = 1; fields["type"] = type; fields["seq"] = ++sequence; fields["ts"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Send(fields);
        }
        public Task Send(object value) => SendRaw(JsonSerializer.Serialize(value));
        public Task SendRaw(string text) => socket.SendAsync(Encoding.UTF8.GetBytes(text).AsMemory(), WebSocketMessageType.Text, true, CancellationToken.None).AsTask();
        public async Task<JsonElement> Receive()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var bytes = new byte[8192]; var count = 0;
            while (true)
            {
                var result = await socket.ReceiveAsync(bytes.AsMemory(count), timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close) throw new Exception("Unexpected WebSocket close.");
                count += result.Count; if (result.EndOfMessage) break;
            }
            using var doc = JsonDocument.Parse(bytes.AsMemory(0, count)); return doc.RootElement.Clone();
        }
        public ValueTask DisposeAsync() { socket.Abort(); socket.Dispose(); return ValueTask.CompletedTask; }
    }
}
