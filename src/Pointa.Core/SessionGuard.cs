namespace Pointa.Core;

/// <summary>Per-connection replay, freshness and bounded message-rate checks.</summary>
public sealed class SessionGuard
{
    private long lastSequence;
    private long windowStarted;
    private int messages;

    public string? Validate(int version, long sequence, long timestamp, long now)
    {
        if (version != 1) return "unsupported_version";
        if (sequence <= lastSequence || sequence <= 0) return "invalid_sequence";
        if (timestamp < now - 2_000 || timestamp > now + 1_000) return "stale_message";
        if (now - windowStarted >= 1_000) { windowStarted = now; messages = 0; }
        if (++messages > 240) return "rate_limited";
        lastSequence = sequence;
        return null;
    }
}
