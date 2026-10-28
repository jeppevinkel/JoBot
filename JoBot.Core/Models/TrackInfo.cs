using System.Text.Json.Serialization;

namespace JoBot.Core.Models;

public record TrackInfo
{
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Album { get; init; }

    /// <summary>Whether DJ mode added this track, rather than someone asking for it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDjPick { get; init; }
}
