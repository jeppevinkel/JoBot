using System.Text.Json.Serialization;

namespace JoBot.Discord.Dj;

/// <summary>What the AI is told about the session when asked for more tracks. Tracks are "Artist - Title".</summary>
public sealed record DjNudgeDetails
{
    [JsonPropertyName("remaining_tracks")]
    public required int RemainingTracks { get; init; }

    [JsonPropertyName("current_track")]
    public string? CurrentTrack { get; init; }

    [JsonPropertyName("queued_tracks")]
    public required IReadOnlyList<string> QueuedTracks { get; init; }

    [JsonPropertyName("tracks_to_add")]
    public required int TracksToAdd { get; init; }

    /// <summary>Style or mood notes from the people listening.</summary>
    [JsonPropertyName("guidance")]
    public string? Guidance { get; init; }

    /// <summary>Oldest first.</summary>
    [JsonPropertyName("recently_played")]
    public required IReadOnlyList<string> RecentlyPlayed { get; init; }

    /// <summary>Tracks people skipped, oldest first.</summary>
    [JsonPropertyName("skipped_tracks")]
    public required IReadOnlyList<string> SkippedTracks { get; init; }
}
