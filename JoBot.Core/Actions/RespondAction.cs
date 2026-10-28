using System.Text.Json.Serialization;

namespace JoBot.Core.Actions;

public record RespondAction : AiAction
{
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// Whether the reply also answers messages that were added to it while it was in progress (see
    /// <see cref="MergedAction"/>), so it may not address the person who started it.
    /// </summary>
    [JsonIgnore]
    public bool IsShared { get; init; }
}