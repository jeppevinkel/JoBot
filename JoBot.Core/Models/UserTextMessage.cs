namespace JoBot.Core.Models;

public class UserTextMessage
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required GuildMember Author { get; init; }
    public required string Content { get; init; }
    public List<GuildMember> Mentions { get; init; } = [];
}