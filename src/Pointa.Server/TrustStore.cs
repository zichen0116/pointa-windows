using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Pointa.Core;

namespace Pointa.Server;

internal sealed record DeviceSecret(PairedDevice Device, string TokenHash);
internal sealed record StoredState(string ServerId, string Certificate, List<DeviceSecret> Devices, LaserEffect Effect);

internal sealed class TrustStore : IDisposable
{
    private readonly string path;
    private StoredState state;
    public X509Certificate2 Certificate { get; }
    public string ServerId => state.ServerId;
    public LaserEffect Effect => state.Effect;
    public IReadOnlyList<PairedDevice> Devices => state.Devices.Select(x => x.Device).ToArray();

    public TrustStore(string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Pointa trust storage requires Windows DPAPI.");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "trust.dat");
        if (File.Exists(path))
        {
            try
            {
                var plaintext = Dpapi.Transform(File.ReadAllBytes(path), false);
                try { state = JsonSerializer.Deserialize<StoredState>(plaintext) ?? throw new InvalidDataException(); }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
                if (!Guid.TryParseExact(state.ServerId, "N", out _) || state.Devices is null || state.Effect is null || !state.Effect.IsValid ||
                    state.Devices.Count > 100 || state.Devices.Any(d => !Guid.TryParseExact(d.Device.Id, "N", out _) ||
                        string.IsNullOrWhiteSpace(d.Device.Name) || Convert.FromHexString(d.TokenHash).Length != 32) ||
                    state.Devices.Select(x => x.Device.Id).Distinct().Count() != state.Devices.Count)
                    throw new InvalidDataException("Invalid trust record.");
                var pfx = Convert.FromBase64String(state.Certificate);
                try { Certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet); }
                finally { CryptographicOperations.ZeroMemory(pfx); }
                if (!Certificate.HasPrivateKey || Certificate.NotAfter <= DateTime.UtcNow)
                    throw new InvalidDataException("Stored certificate is missing its key or expired.");
            }
            catch (Exception ex) when (ex is not IOException and not UnauthorizedAccessException)
            {
                throw new InvalidDataException("Pointa trust.dat cannot be read. Preserve this file; restore a backup or explicitly reset trust.", ex);
            }
        }
        else
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Pointa local presentation", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
            var pfx = generated.Export(X509ContentType.Pkcs12);
            // Windows SChannel needs a user key container; its temporary import is removed on disposal.
            Certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
            state = new(Guid.NewGuid().ToString("N"), Convert.ToBase64String(pfx), [], new());
            CryptographicOperations.ZeroMemory(pfx);
            Save(state);
        }
    }

    public PairedDevice? Authenticate(string id, string token)
    {
        if (token.Length != 43) return null;
        var entry = state.Devices.FirstOrDefault(x => x.Device.Id == id);
        if (entry is null || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(entry.TokenHash), SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))) return null;
        return entry.Device;
    }

    public (PairedDevice Device, string Token) Add(string name)
    {
        if (state.Devices.Count >= 100) throw new InvalidOperationException("device_limit");
        var now = DateTimeOffset.UtcNow;
        var device = new PairedDevice(Guid.NewGuid().ToString("N"), name, now, now);
        var token = RandomToken();
        var devices = state.Devices.Append(new DeviceSecret(device, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))))).ToList();
        Save(state with { Devices = devices });
        return (device, token);
    }

    public PairedDevice Touch(string id)
    {
        var devices = state.Devices.Select(x => x.Device.Id == id ? x with { Device = x.Device with { LastConnectedAt = DateTimeOffset.UtcNow } } : x).ToList();
        Save(state with { Devices = devices });
        return devices.Single(x => x.Device.Id == id).Device;
    }
    public void Revoke(string id) => Save(state with { Devices = state.Devices.Where(x => x.Device.Id != id).ToList() });
    public void Clear() => Save(state with { Devices = [] });
    public void SetEffect(LaserEffect effect) => Save(state with { Effect = effect });
    public static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void Save(StoredState replacement)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(replacement);
        byte[] protectedBytes;
        try { protectedBytes = Dpapi.Transform(raw, true); }
        finally { CryptographicOperations.ZeroMemory(raw); }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(protectedBytes);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
            state = replacement;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() => Certificate.Dispose();
}

internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, "Pointa", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Length], 0, input.Data, input.Length);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
        }
    }
}
