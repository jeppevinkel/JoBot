using System.Text.Json.Serialization;

namespace JoBot.Core.Actions;

public record RespondAction : AiAction
{
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}