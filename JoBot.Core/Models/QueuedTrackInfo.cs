namespace JoBot.Core.Models;

public record QueuedTrackInfo : TrackInfo
{
    /// <summary>1-based position in the queue, where 1 is the next track to play.</summary>
    public required int Position { get; init; }
}
