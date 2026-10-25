namespace JoBot.Core.Models;

public record QueueInfo
{
    public TrackInfo? CurrentTrack { get; init; }
    public IReadOnlyList<QueuedTrackInfo> QueuedTracks { get; init; } = [];
    public int Total => QueuedTracks.Count;
}