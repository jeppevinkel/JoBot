using System.Text.Json.Serialization;

namespace JoBot.Ai.History.Serialization;

public record StoredContentBlock
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    // text
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    // tool_use
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("input_json")]
    public string? InputJson { get; init; }

    // tool_result
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("is_error")]
    public bool? IsError { get; init; }
}