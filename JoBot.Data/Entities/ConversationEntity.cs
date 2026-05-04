namespace JoBot.Data.Entities;

public class ConversationEntity
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}