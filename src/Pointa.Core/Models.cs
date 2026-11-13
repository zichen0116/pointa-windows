namespace Pointa.Core;

public sealed record PointerState(double X, double Y, bool Active, long Revision = 0);
public sealed record DisplayInfo(int Width, int Height);
public sealed record LaserEffect(string Color = "#FF6475", int Size = 12, int TrailMs = 500, string Smoothing = "standard")
{
    public bool IsValid => Color is { Length: 7 } && Color[0] == '#' && Color.Skip(1).All(Uri.IsHexDigit) &&
        Size is >= 6 and <= 40 && TrailMs is >= 0 and <= 1200 && Smoothing is "low" or "standard" or "high";
}
public enum SlideDirection { Previous, Next }
public sealed record SlideResult(bool Success, string? Error = null);
public sealed record PairedDevice(string Id, string Name, DateTimeOffset PairedAt, DateTimeOffset LastConnectedAt);
public sealed record PairingRequest(string DeviceName, string RemoteAddress, string Method);
public sealed record PairingInfo(string ServerId, string DeviceName, string Host, int Port, string CertificateSha256,
    string QrToken, string Code, string PairingId, DateTimeOffset ExpiresAt, string QrPayload);
