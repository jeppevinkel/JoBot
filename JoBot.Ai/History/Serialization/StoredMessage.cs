using System.Text.Json.Serialization;

namespace JoBot.Ai.History.Serialization;

public record StoredMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required List<StoredContentBlock> Content { get; init; }
}