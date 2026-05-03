namespace JoBot.Core.Models;

public class QueuedTrack
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public required string Url { get; init; }
    public TimeSpan? Duration { get; init; }
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Discord user ID of the person who queued this track.</summary>
    public ulong RequestedByUserId { get; init; }

    /// <summary>Discord username of the person who queued this track.</summary>
    public string? RequestedByUsername { get; init; }

    /// <summary>
    /// The audio stream of the track.
    /// </summary>
    public Stream? Stream { get; set; }
}