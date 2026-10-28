using System.Text.Json.Serialization;

namespace JoBot.Core.Models;

/// <summary>
/// Something the bot itself noticed and wants the AI to act on, such as DJ mode's queue running low.
/// Unlike <see cref="MessagePayload"/>, no person wrote it.
/// </summary>
public record AutomatedEventPayload
{
    [JsonPropertyName("source")]
    public required SourceInfo Source { get; init; }

    /// <summary>What happened, e.g. <c>dj_mode_queue_low</c>.</summary>
    [JsonPropertyName("event")]
    public required string Event { get; init; }

    /// <summary>Instructions for how to handle the event.</summary>
    [JsonPropertyName("note")]
    public required string Note { get; init; }

    /// <summary>Event-specific context, serialized as a nested object.</summary>
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Details { get; init; }
}
