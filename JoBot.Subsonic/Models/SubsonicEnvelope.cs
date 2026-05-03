using System.Text.Json.Serialization;

namespace JoBot.Subsonic.Models;

public class SubsonicEnvelope
{
    [JsonPropertyName("subsonic-response")]
    public SubsonicResponse? Response { get; init; }
}