using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pointa.Core;

namespace Pointa.Server;

public sealed record HostOptions(string DataDirectory, IReadOnlyList<IPAddress> BindAddresses,
    string DeviceName = "Pointa", int Port = 32145, int DiscoveryPort = 32146);

public sealed class HostService : IAsyncDisposable
{
    private readonly HostOptions options;
    private readonly Func<PairingRequest, CancellationToken, Task<bool>> approve;
    private readonly object gate = new();
    private readonly TrustStore store;
    private readonly SemaphoreSlim authenticationSlots = new(4, 4);
    private readonly SemaphoreSlim pointerSignal = new(0, 1);
    private readonly Queue<long> authStarts = new();
    private readonly Dictionary<string, long> actionIds = [];
    private readonly string certificatePin;
    private PairingInfo pairing = null!;
    private bool pairingReserved;
    private int codeAttempts;
    private WebApplication? app;
    private CancellationTokenSource? lifetime;
    private Task[] background = [];
    private UdpClient? discovery;
    private Controller? controller;
    private PointerState latestPointer = new(.5, .5, false);
    private long lastSlide;
    private long lastRemoteEffect;

    public event Action<PointerState>? PointerChanged;
    public event Action<LaserEffect>? EffectChanged;
    public event Action? StateChanged;
    public event Action<string>? Diagnostic;
    public Func<SlideDirection, CancellationToken, SlideResult>? SlideRequested { get; set; }
    public PairingInfo Pairing { get { lock (gate) return pairing; } }
    public IReadOnlyList<PairedDevice> Devices { get { lock (gate) return store.Devices; } }
    public PairedDevice? ConnectedDevice { get { lock (gate) return controller?.Device; } }
    public LaserEffect Effect { get { lock (gate) return store.Effect; } }

    public HostService(HostOptions options, Func<PairingRequest, CancellationToken, Task<bool>> approve)
    {
        if (options.BindAddresses.Count == 0 || options.BindAddresses.Any(x => !IsLocal(x)) || options.Port is < 1 or > 65535 ||
            options.DiscoveryPort is < 0 or > 65535 || string.IsNullOrWhiteSpace(options.DeviceName))
            throw new ArgumentException("Use explicit loopback/private IPv4 addresses and valid ports.", nameof(options));
        this.options = options with { BindAddresses = options.BindAddresses.Distinct().ToArray(), DeviceName = options.DeviceName.Trim()[..Math.Min(options.DeviceName.Trim().Length, 64)] };
        this.approve = approve;
        store = new TrustStore(options.DataDirectory);
        certificatePin = Convert.ToHexString(SHA256.HashData(store.Certificate.RawData)).ToLowerInvariant();
        RefreshPairing();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (app is not null) return;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Limits.MaxConcurrentConnections = 16;
            server.Limits.MaxRequestBodySize = 4096;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            foreach (var address in options.BindAddresses)
                server.Listen(address, options.Port, listener => listener.UseHttps(https =>
                {
                    https.ServerCertificate = store.Certificate;
                    https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                }));
        });
        var application = builder.Build();
        application.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });
        application.Map("/ws", HandleConnectionAsync);
        lifetime = new CancellationTokenSource();
        try { await application.StartAsync(cancellationToken); }
        catch { await application.DisposeAsync(); lifetime.Dispose(); lifetime = null; throw; }
        app = application;
        background = [RunMaintenanceAsync(lifetime.Token), RunPointerAsync(lifetime.Token), RunDiscoveryAsync(lifetime.Token)];
        StateChanged?.Invoke();
    }

    public async Task StopAsync()
    {
        Disconnect();
        if (app is null) return;
        lifetime!.Cancel();
        discovery?.Dispose();
        try { await Task.WhenAll(background); } catch (OperationCanceledException) { }
        await app.StopAsync();
        await app.DisposeAsync();
        app = null;
        lifetime.Dispose();
        lifetime = null;
        StateChanged?.Invoke();
    }

    public void RefreshPairing()
    {
        lock (gate) RefreshPairingLocked();
        StateChanged?.Invoke();
    }

    private void RefreshPairingLocked()
    {
            var id = Guid.NewGuid().ToString("N");
            var token = TrustStore.RandomToken();
            var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var host = options.BindAddresses.FirstOrDefault(x => !IPAddress.IsLoopback(x)) ?? options.BindAddresses[0];
            var uri = $"pointa://pair?v=1&host={host}&port={options.Port}&id={store.ServerId}&pair={id}&pin={certificatePin}&token={token}";
            pairing = new(store.ServerId, options.DeviceName, host.ToString(), options.Port, certificatePin, token, code, id, DateTimeOffset.UtcNow.AddSeconds(120), uri);
            pairingReserved = false;
            codeAttempts = 0;
    }

    public void Disconnect()
    {
        PointerState hidden;
        lock (gate)
        {
            controller?.Cancellation.Cancel();
            controller?.Socket.Abort();
            controller = null;
            hidden = HidePointerLocked();
        }
        PointerChanged?.Invoke(hidden);
        StateChanged?.Invoke();
    }

    public void RevokeDevice(string id)
    {
        PointerState? hidden = null;
        lock (gate)
        {
            store.Revoke(id);
            if (controller?.Device.Id == id)
            {
                controller.Cancellation.Cancel();
                controller.Socket.Abort();
                controller = null;
                hidden = HidePointerLocked();
            }
        }
        if (hidden is not null) PointerChanged?.Invoke(hidden);
        StateChanged?.Invoke();
    }

    public void ClearDevices()
    {
        PointerState hidden;
        lock (gate)
        {
            store.Clear();
            RefreshPairingLocked();
            controller?.Cancellation.Cancel();
            controller?.Socket.Abort();
            controller = null;
            hidden = HidePointerLocked();
        }
        PointerChanged?.Invoke(hidden);
        StateChanged?.Invoke();
    }

    public void SetEffect(LaserEffect effect)
    {
        if (!effect.IsValid) throw new ArgumentException("Invalid laser effect.", nameof(effect));
        lock (gate) { if (store.Effect == effect) return; store.SetEffect(effect); }
        EffectChanged?.Invoke(effect);
    }

    private async Task HandleConnectionAsync(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is not { } remoteAddress || !IsLocal(remoteAddress))
        { context.Response.StatusCode = 403; return; }
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        // Browser origins are unsupported: a website must not silently probe or control the local service.
        if (context.Request.Headers.ContainsKey("Origin")) { context.Response.StatusCode = 403; return; }
        if (!await authenticationSlots.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 429; return; }
        WebSocket accepted;
        try { accepted = await context.WebSockets.AcceptWebSocketAsync(); }
        catch { authenticationSlots.Release(); throw; }
        using var socket = accepted;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime?.Token ?? CancellationToken.None);
        Controller? session = null;
        var ownsSlot = true;
        try
        {
            lock (gate)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                while (authStarts.TryPeek(out var time) && now - time >= 60_000) authStarts.Dequeue();
                if (authStarts.Count >= 30) throw new ProtocolException("auth_rate_limited");
                authStarts.Enqueue(now);
            }
            using var auth = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            auth.CancelAfter(TimeSpan.FromSeconds(60));
            await SendAsync(socket, new { v = 1, type = "hello", serverId = store.ServerId, name = options.DeviceName,
                pairingId = Pairing.PairingId, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), heartbeatMs = 1000, maxMessageBytes = 4096 }, auth.Token);
            var device = await AuthenticateAsync(socket, context.Connection.RemoteIpAddress?.ToString() ?? "unknown", auth.Token);
            PointerState hidden;
            lock (gate)
            {
                if (controller is not null) throw new ProtocolException("controller_busy");
                // Revocation can race an approval or the last authentication response.
                if (!store.Devices.Any(x => x.Id == device.Id)) throw new ProtocolException("credential_revoked");
                device = store.Touch(device.Id);
                controller = session = new(socket, cancellation, device);
                hidden = HidePointerLocked();
            }
            PointerChanged?.Invoke(hidden);
            authenticationSlots.Release(); ownsSlot = false;
            await SendAsync(socket, new { v = 1, type = "session.ready", deviceId = device.Id, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), effect = Effect }, cancellation.Token);
            StateChanged?.Invoke();
            var guard = new SessionGuard();
            while (!cancellation.IsCancellationRequested)
            {
                using var document = await ReadAsync(socket, TimeSpan.FromSeconds(4), cancellation.Token);
                var root = document.RootElement;
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var error = guard.Validate(Integer(root, "v"), Long(root, "seq"), Long(root, "ts"), now);
                if (error is not null) throw new ProtocolException(error);
                object? response;
                Func<object?>? dispatch;
                lock (gate)
                {
                    if (controller != session) throw new ProtocolException("credential_revoked");
                    session.LastReceived = now;
                    response = HandleControl(root, now, session, out dispatch);
                }
                // UI callbacks may synchronously read host state; never invoke them while holding gate.
                if (dispatch is not null) response = dispatch();
                if (response is not null) await SendAsync(socket, response, cancellation.Token);
            }
        }
        catch (ProtocolException ex) { await TryErrorAsync(socket, ex.Message); }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or Org.BouncyCastle.Crypto.CryptoException)
        { await TryErrorAsync(socket, "invalid_message"); }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException) { }
        finally
        {
            if (ownsSlot) authenticationSlots.Release();
            PointerState? hidden = null;
            lock (gate) { if (controller == session && session is not null) { controller = null; hidden = HidePointerLocked(); } }
            if (hidden is not null) PointerChanged?.Invoke(hidden);
            if (socket.State == WebSocketState.Open)
            {
                using var close = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "session ended", close.Token); } catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
            }
            StateChanged?.Invoke();
        }
    }

    private async Task<PairedDevice> AuthenticateAsync(WebSocket socket, string remote, CancellationToken ct)
    {
        using var first = await ReadAsync(socket, TimeSpan.FromSeconds(15), ct);
        var root = first.RootElement;
        if (Integer(root, "v") != 1) throw new ProtocolException("unsupported_version");
        var type = Text(root, "type", 32);
        if (type == "auth.resume")
        {
            lock (gate) return store.Authenticate(Text(root, "deviceId", 32), Text(root, "credential", 43)) ?? throw new ProtocolException("invalid_credential");
        }
        var name = Text(root, "name", 64).Trim();
        if (name.Length == 0 || name.Any(char.IsControl)) throw new ProtocolException("invalid_name");
        var requestedPairing = Text(root, "pairingId", 32);
        if (type == "pair.qr")
        {
            lock (gate)
            {
                CheckPairing(requestedPairing);
                var token = Text(root, "token", 43);
                if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(pairing.QrToken)))
                    throw new ProtocolException("invalid_pairing");
                pairingReserved = true;
            }
        }
        else if (type == "pair.srp.begin")
        {
            SrpPairing srp;
            lock (gate)
            {
                CheckPairing(requestedPairing);
                if (++codeAttempts > 5) throw new ProtocolException("code_rate_limited");
                srp = new(store.ServerId, pairing.PairingId, certificatePin, pairing.Code);
            }
            await SendAsync(socket, new { v = 1, type = "pair.srp.challenge", salt = srp.Salt, b = srp.B, group = "rfc5054-3072", hash = "SHA-256" }, ct);
            using var proof = await ReadAsync(socket, TimeSpan.FromSeconds(15), ct);
            RequireType(proof.RootElement, "pair.srp.proof");
            var m2 = srp.Prove(Text(proof.RootElement, "a", 768), Text(proof.RootElement, "m1", 64));
            await SendAsync(socket, new { v = 1, type = "pair.srp.server", m2 }, ct);
            using var confirmation = await ReadAsync(socket, TimeSpan.FromSeconds(15), ct);
            RequireType(confirmation.RootElement, "pair.srp.confirm");
            if (!srp.Confirm(Text(confirmation.RootElement, "confirmation", 64))) throw new ProtocolException("invalid_proof");
            lock (gate) { CheckPairing(requestedPairing); pairingReserved = true; }
        }
        else throw new ProtocolException("authentication_required");

        await SendAsync(socket, new { v = 1, type = "pair.pending" }, ct);
        using var approval = CancellationTokenSource.CreateLinkedTokenSource(ct);
        approval.CancelAfter(TimeSpan.FromSeconds(30));
        if (!await approve(new(name, remote, type == "pair.qr" ? "qr" : "code"), approval.Token).WaitAsync(approval.Token))
            throw new ProtocolException("pairing_denied");
        PairedDevice device;
        string credential;
        lock (gate)
        {
            if (pairing.PairingId != requestedPairing || DateTimeOffset.UtcNow >= pairing.ExpiresAt) throw new ProtocolException("pairing_expired");
            (device, credential) = store.Add(name);
            RefreshPairingLocked();
        }
        StateChanged?.Invoke();
        await SendAsync(socket, new { v = 1, type = "pair.approved", deviceId = device.Id, credential, serverId = store.ServerId, certificateSha256 = certificatePin }, ct);
        return device;
    }

    private object? HandleControl(JsonElement root, long now, Controller session, out Func<object?>? dispatch)
    {
        dispatch = null;
        var type = Text(root, "type", 32);
        switch (type)
        {
            case "heartbeat": return new { v = 1, type = "heartbeat.ack", time = now };
            case "pointer.move":
                var x = Number(root, "x"); var y = Number(root, "y");
                if (x is < 0 or > 1 || y is < 0 or > 1 || !root.TryGetProperty("active", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ProtocolException("invalid_pointer");
                latestPointer = new(x, y, active.GetBoolean(), latestPointer.Revision + 1);
                if (!latestPointer.Active)
                {
                    var hidden = latestPointer;
                    dispatch = () => { PointerChanged?.Invoke(hidden); return null; };
                }
                else if (pointerSignal.CurrentCount == 0) pointerSignal.Release();
                return null;
            case "pointer.hide":
                var hiddenPointer = HidePointerLocked();
                dispatch = () => { PointerChanged?.Invoke(hiddenPointer); return null; };
                return null;
            case "slide.prev":
            case "slide.next":
                var id = Text(root, "id", 36);
                if (!Guid.TryParseExact(id, "D", out var actionId)) throw new ProtocolException("invalid_action_id");
                id = actionId.ToString("D");
                foreach (var stale in actionIds.Where(x => now - x.Value > 120_000).Select(x => x.Key).ToArray()) actionIds.Remove(stale);
                if (!actionIds.TryAdd(id, now)) return new { v = 1, type = "action.result", id, success = false, error = "duplicate_action" };
                if (now - lastSlide < 180) return new { v = 1, type = "action.result", id, success = false, error = "action_cooldown" };
                lastSlide = now;
                dispatch = () =>
                {
                    var result = session.Cancellation.IsCancellationRequested ? new SlideResult(false, "credential_revoked") :
                        SlideRequested?.Invoke(type == "slide.next" ? SlideDirection.Next : SlideDirection.Previous, session.Cancellation.Token) ?? new(false, "no_presentation_adapter");
                    return new { v = 1, type = "action.result", id, success = result.Success, error = result.Error };
                };
                return null;
            case "effect.update":
                if (now - lastRemoteEffect < 250) return new { v = 1, type = "effect.rejected", error = "effect_cooldown" };
                var effect = new LaserEffect(Text(root, "color", 7), Integer(root, "size"), Integer(root, "trailMs"), Text(root, "smoothing", 16));
                if (!effect.IsValid) throw new ProtocolException("invalid_effect");
                lastRemoteEffect = now;
                store.SetEffect(effect);
                dispatch = () => { EffectChanged?.Invoke(effect); return new { v = 1, type = "effect.applied", effect }; };
                return null;
            case "device.forget":
                store.Revoke(session.Device.Id);
                session.Cancellation.Cancel();
                session.Socket.Abort();
                controller = null;
                var forgotten = HidePointerLocked();
                dispatch = () => { PointerChanged?.Invoke(forgotten); StateChanged?.Invoke(); return null; };
                return null;
            default: throw new ProtocolException("unknown_message");
        }
    }

    private void CheckPairing(string id)
    {
        if (pairingReserved || pairing.PairingId != id || DateTimeOffset.UtcNow >= pairing.ExpiresAt) throw new ProtocolException("pairing_expired");
    }

    private PointerState HidePointerLocked()
    {
        return latestPointer = latestPointer with { Active = false, Revision = latestPointer.Revision + 1 };
    }

    private async Task RunPointerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await pointerSignal.WaitAsync(ct);
            await Task.Delay(8, ct);
            PointerState? pointer;
            lock (gate) pointer = controller is not null ? latestPointer : null;
            if (pointer is not null) PointerChanged?.Invoke(pointer);
        }
    }

    private async Task RunMaintenanceAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            bool changed = false;
            PointerState? hidden = null;
            lock (gate)
            {
                if (DateTimeOffset.UtcNow >= pairing.ExpiresAt) { RefreshPairingLocked(); changed = true; }
                if (controller is not null && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - controller.LastReceived > 3_000)
                {
                    controller.Cancellation.Cancel();
                    controller.Socket.Abort();
                    controller = null;
                    hidden = HidePointerLocked();
                    changed = true;
                }
            }
            if (hidden is not null) PointerChanged?.Invoke(hidden);
            if (changed) StateChanged?.Invoke();
        }
    }

    private async Task RunDiscoveryAsync(CancellationToken ct)
    {
        if (options.DiscoveryPort == 0) return;
        try
        {
            discovery = new UdpClient(new IPEndPoint(IPAddress.Any, options.DiscoveryPort));
            discovery.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            var buffer = new byte[256];
            long window = 0; int replies = 0;
            while (!ct.IsCancellationRequested)
            {
                var result = await discovery.Client.ReceiveMessageFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct);
                var remote = (IPEndPoint)result.RemoteEndPoint;
                // UDP binds Any for broadcasts; reject packets unless their incoming interface was explicitly approved.
                if (!IsLocal(remote.Address) || !IncomingInterfaceAllowed(result.PacketInformation.Interface) || result.ReceivedBytes != 19 ||
                    Encoding.ASCII.GetString(buffer, 0, result.ReceivedBytes) != "POINTA_DISCOVER_V1\n") continue;
                var now = Environment.TickCount64;
                if (now - window > 1000) { window = now; replies = 0; }
                if (++replies > 10) continue;
                var snapshot = Pairing;
                var payload = JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "pointa.discovery", serverId = snapshot.ServerId, name = options.DeviceName,
                    port = options.Port, pairingId = snapshot.PairingId, expiresAt = snapshot.ExpiresAt.ToUnixTimeMilliseconds() });
                await discovery.SendAsync(payload, remote, ct);
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        { if (!ct.IsCancellationRequested) Diagnostic?.Invoke("UDP discovery unavailable: " + ex.Message); }
    }

    private bool IncomingInterfaceAllowed(int index)
    {
        foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            var properties = adapter.GetIPProperties();
            if (properties.GetIPv4Properties()?.Index == index && properties.UnicastAddresses.Any(x => options.BindAddresses.Contains(x.Address))) return true;
        }
        return false;
    }

    private static bool IsLocal(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return IPAddress.IsLoopback(address) || bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }
    private static void RequireType(JsonElement root, string type)
    {
        if (Integer(root, "v") != 1 || Text(root, "type", 32) != type) throw new ProtocolException("invalid_handshake");
    }
    private static string Text(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String || field.GetString() is not { } value || value.Length == 0 || value.Length > maximum)
            throw new ProtocolException("invalid_" + name);
        return value;
    }
    private static int Integer(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var field) || !field.TryGetInt32(out var value)) throw new ProtocolException("invalid_" + name);
        return value;
    }
    private static long Long(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var field) || !field.TryGetInt64(out var value)) throw new ProtocolException("invalid_" + name);
        return value;
    }
    private static double Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var field) || !field.TryGetDouble(out var value) || !double.IsFinite(value)) throw new ProtocolException("invalid_" + name);
        return value;
    }

    private static async Task<JsonDocument> ReadAsync(WebSocket socket, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var bytes = new byte[4097];
        var count = 0;
        while (true)
        {
            var message = await socket.ReceiveAsync(bytes.AsMemory(count), deadline.Token);
            if (message.MessageType == WebSocketMessageType.Close) throw new OperationCanceledException();
            if (message.MessageType != WebSocketMessageType.Text) throw new ProtocolException("text_required");
            count += message.Count;
            if (count > 4096) throw new ProtocolException("message_too_large");
            if (message.EndOfMessage) break;
        }
        var document = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) { document.Dispose(); throw new ProtocolException("invalid_message"); }
        var properties = document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
        if (properties.Distinct(StringComparer.Ordinal).Count() != properties.Length) { document.Dispose(); throw new ProtocolException("duplicate_property"); }
        return document;
    }
    private static Task SendAsync(WebSocket socket, object value, CancellationToken ct) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions).AsMemory(), WebSocketMessageType.Text, true, ct).AsTask();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static async Task TryErrorAsync(WebSocket socket, string code)
    {
        if (socket.State != WebSocketState.Open) return;
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await SendAsync(socket, new { v = 1, type = "error", code }, ct.Token); }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
    }
    public async ValueTask DisposeAsync() { await StopAsync(); store.Dispose(); authenticationSlots.Dispose(); pointerSignal.Dispose(); }

    private sealed class Controller(WebSocket socket, CancellationTokenSource cancellation, PairedDevice device)
    {
        public WebSocket Socket { get; } = socket;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public PairedDevice Device { get; } = device;
        public long LastReceived { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
