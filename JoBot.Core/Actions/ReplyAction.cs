using System.Text.Json.Serialization;

namespace JoBot.Core.Actions;

public record ReplyAction : AiAction
{
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}