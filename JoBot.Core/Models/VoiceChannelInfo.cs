namespace JoBot.Core.Models;

public record VoiceChannelInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Category { get; init; }
    public int ConnectedUsers { get; init; }

    /// <summary>Maximum number of users, or null if unlimited.</summary>
    public int? UserLimit { get; init; }

    /// <summary>Whether the bot has permission to connect to the channel.</summary>
    public bool CanJoin { get; init; }

    public bool IsBotConnected { get; init; }
}
