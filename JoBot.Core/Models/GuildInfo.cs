using System.Text.Json.Serialization;

namespace JoBot.Core.Models;

public record GuildInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}