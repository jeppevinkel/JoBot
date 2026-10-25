namespace JoBot.Core.Models;

public record NowPlayingInfo
{
    /// <summary>The current music track, or null if nothing is playing.</summary>
    public TrackInfo? Track { get; init; }
    public TimeSpan? Position { get; init; }
    public bool IsPaused { get; init; }

    /// <summary>Whether a TTS clip is temporarily interrupting the music.</summary>
    public bool IsPlayingTts { get; init; }
}
