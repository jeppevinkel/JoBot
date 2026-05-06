namespace JoBot.TextToSpeech.Models;

internal sealed class TtsAudioEntry
{
    // If never accessed, expire after 30 minutes
    private static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(30);
    // After each access, reset to 15 minutes
    private static readonly TimeSpan ActiveTtl = TimeSpan.FromMinutes(15);

    public byte[] Audio { get; }

    // Store as ticks for lock-free atomic reads/writes via Interlocked
    private long _expiresAtTicks;

    public TtsAudioEntry(byte[] audio)
    {
        Audio = audio;
        _expiresAtTicks = (DateTimeOffset.UtcNow + IdleTtl).UtcTicks;
    }

    /// <summary>
    /// Called each time the entry is served. Resets the expiry to the active TTL.
    /// </summary>
    public void Touch()
    {
        Interlocked.Exchange(ref _expiresAtTicks, (DateTimeOffset.UtcNow + ActiveTtl).UtcTicks);
    }

    public bool IsExpired =>
        DateTimeOffset.UtcNow.UtcTicks >= Interlocked.Read(ref _expiresAtTicks);
}