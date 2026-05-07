using System.Text.Json.Serialization;

namespace JoBot.Core.Models;

public record MessagePayload
{
    [JsonPropertyName("source")]
    public required SourceInfo Source { get; init; }

    [JsonPropertyName("author")]
    public required UserInfo Author { get; init; }

    [JsonPropertyName("message")]
    public required MessageInfo Message { get; init; }

    [JsonPropertyName("referenced_message")]
    public MessageInfo? ReferencedMessage { get; init; }

    [JsonPropertyName("mentioned_users")]
    public IReadOnlyList<UserInfo> MentionedUsers { get; init; } = [];

    [JsonPropertyName("is_first_after_reboot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFirstAfterReboot { get; init; }
}