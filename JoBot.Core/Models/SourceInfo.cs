using System.Text.Json.Serialization;

namespace JoBot.Core.Models;

public record SourceInfo
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("guild")]
    public GuildInfo? Guild { get; init; }

    [JsonPropertyName("channel")]
    public required ChannelInfo Channel { get; init; }
}