namespace JoBot.Core.Models;

public record TrackInfo
{
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required TimeSpan Duration { get; init; }
}