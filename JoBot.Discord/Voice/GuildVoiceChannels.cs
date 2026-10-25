using DSharpPlus;
using DSharpPlus.Entities;
using JoBot.Core.Models;

namespace JoBot.Discord.Voice;

public static class GuildVoiceChannels
{
    /// <summary>
    /// Lists the voice channels in a guild that the bot can see, or returns null if the guild isn't cached.
    /// Uses the gateway cache, so this doesn't make any API requests.
    /// </summary>
    /// <param name="botChannelId">The voice channel the bot is connected to, if any.</param>
    public static IReadOnlyList<VoiceChannelInfo>? List(DiscordClient client, ulong guildId, ulong? botChannelId)
    {
        if (!client.Guilds.TryGetValue(guildId, out DiscordGuild? guild))
            return null;

        DiscordMember? bot = guild.CurrentMember;

        var userCounts = guild.VoiceStates.Values
            .Where(state => state.ChannelId is not null)
            .GroupBy(state => state.ChannelId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        return guild.Channels.Values
            .Where(channel => channel.Type is DiscordChannelType.Voice or DiscordChannelType.Stage)
            .Select(channel => (Channel: channel, Permissions: bot is null ? (DiscordPermissions?)null : channel.PermissionsFor(bot)))
            // Don't reveal channels hidden from the bot
            .Where(x => x.Permissions?.HasPermission(DiscordPermission.ViewChannel) ?? false)
            .OrderBy(x => x.Channel.Parent?.Position ?? -1)
            .ThenBy(x => x.Channel.Position)
            .Select(x => new VoiceChannelInfo
            {
                Id = x.Channel.Id.ToString(),
                Name = x.Channel.Name,
                Category = x.Channel.Parent?.Name,
                ConnectedUsers = userCounts.GetValueOrDefault(x.Channel.Id),
                UserLimit = x.Channel.UserLimit is > 0 ? x.Channel.UserLimit : null,
                CanJoin = x.Permissions!.Value.HasPermission(DiscordPermission.Connect),
                IsBotConnected = botChannelId == x.Channel.Id
            })
            .ToList();
    }
}
