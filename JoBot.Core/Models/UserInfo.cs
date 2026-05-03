using System.Text.Json.Serialization;

namespace JoBot.Core.Models;

public record UserInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }
    
    [JsonPropertyName("voice_channel")]
    public ChannelInfo? VoiceChannel { get; init; }
}