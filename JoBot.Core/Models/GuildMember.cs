namespace JoBot.Core.Models;

public class GuildMember
{
    public required ulong UserId { get; init; }
    public required string Username { get; init; }
    public string Mention => $"<@{UserId}>";
}