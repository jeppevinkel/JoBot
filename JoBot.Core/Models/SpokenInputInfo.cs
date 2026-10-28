using System.Text.Json.Serialization;

namespace JoBot.Core.Models;

/// <summary>Describes a message that was spoken in a voice channel and transcribed, rather than typed.</summary>
public record SpokenInputInfo
{
    /// <summary>Whether the speaker said the bot's name, as opposed to following up on its last reply.</summary>
    [JsonPropertyName("addressed_by_name")]
    public required bool AddressedByName { get; init; }

    /// <summary>Instructions for how to treat and answer the transcript.</summary>
    [JsonPropertyName("note")]
    public required string Note { get; init; }
}
