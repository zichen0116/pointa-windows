using System.Net;
using System.Text.Json;
using Pointa.Core;
using Pointa.Server;

// Test-only adapter: no native UI/input, LAN listener, persistent production trust, or discovery.
internal static class Program
{
    private static readonly object outputGate = new();
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main(string[] args)
    {
        var port = 32155;
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--port" || !int.TryParse(args[1], out port) || port is < 1 or > 65535))
        {
            Console.Error.WriteLine("Usage: Pointa.InteropHost [--port 32155]");
            return 2;
        }
        var directory = Path.Combine(Path.GetTempPath(), "Pointa-interop-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var host = new HostService(new(directory, [IPAddress.Loopback], "Pointa interoperability test", port, 0), (request, _) =>
            {
                Write(new { @event = "approval", request.DeviceName, request.Method });
                return Task.FromResult(true);
            });
            var pointer = new PointerState(.5, .5, false);
            var slides = 0;
            host.PointerChanged += state =>
            {
                Interlocked.Exchange(ref pointer, state);
                Write(new { @event = "pointer", state.X, state.Y, state.Active, state.Revision });
            };
            host.SlideRequested = (direction, cancellation) =>
            {
                if (cancellation.IsCancellationRequested) return new(false, "credential_revoked");
                var count = Interlocked.Increment(ref slides);
                Write(new { @event = "slide", direction = direction == SlideDirection.Next ? "next" : "prev", count });
                return new(true);
            };
            host.EffectChanged += effect => Write(new { @event = "effect", effect });
            host.StateChanged += () => Write(new { @event = "state", connectedDeviceId = host.ConnectedDevice?.Id });
            await host.StartAsync();
            Fixture(host, "ready");
            while (await Console.In.ReadLineAsync() is { } line)
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var command = root.GetProperty("command").GetString();
                    switch (command)
                    {
                        case "fixture": Fixture(host, "fixture"); break;
                        case "refresh": host.RefreshPairing(); Fixture(host, "fixture"); break;
                        case "disconnect": host.Disconnect(); break;
                        case "revoke": host.RevokeDevice(root.GetProperty("deviceId").GetString()!); break;
                        case "display":
                            host.SetDisplay(root.TryGetProperty("width", out var width) && width.ValueKind != JsonValueKind.Null
                                ? new(width.GetInt32(), root.GetProperty("height").GetInt32()) : null);
                            break;
                        case "state":
                            Write(new { @event = "snapshot", connectedDeviceId = host.ConnectedDevice?.Id, devices = host.Devices,
                                pointer = Volatile.Read(ref pointer), slides = Volatile.Read(ref slides), display = host.Display, effect = host.Effect });
                            break;
                        case "quit": Write(new { @event = "command", command }); return 0;
                        default: throw new ArgumentException("Unknown command.");
                    }
                    Write(new { @event = "command", command });
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
                {
                    Write(new { @event = "command.error", message = ex.Message });
                }
            }
            return 0;
        }
        finally
        {
            // This is the freshly generated test directory, never an argument or production path.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Fixture(HostService host, string eventName)
    {
        var pair = host.Pairing;
        Write(new { @event = eventName, qr = pair.QrPayload, pair.Code, pair.CertificateSha256, pair.ServerId,
            pair.PairingId, pair.Port, pair.Host, expiresAt = pair.ExpiresAt.ToUnixTimeMilliseconds() });
    }

    private static void Write(object value)
    {
        lock (outputGate) Console.WriteLine(JsonSerializer.Serialize(value, json));
    }
}
