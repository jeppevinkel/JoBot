namespace JoBot.Core.Models;

public record ConversationMessage
{
    public required string Role { get; init; }
    public required string ContentJson { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}