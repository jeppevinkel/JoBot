using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using JoBot.Core.Models;
using JoBot.Discord.Resolvers;

namespace JoBot.Discord.Builders;

public class MessagePayloadBuilder : IMessagePayloadBuilder
{
    private readonly IDisplayNameResolver _resolver;

    public MessagePayloadBuilder(IDisplayNameResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<MessagePayload> BuildAsync(MessageCreatedEventArgs eventArgs)
    {
        DiscordGuild? guild = eventArgs.Guild;
        var authorName = await _resolver.ResolveAsync(eventArgs.Author, guild);
        var mentionedNames = await _resolver.ResolveManyAsync(eventArgs.MentionedUsers, guild);

        ChannelInfo? authorVoiceChannel = await ResolveVoiceChannelAsync(guild, eventArgs.Author.Id);
        var mentionedVoiceChannels = await ResolveManyVoiceChannelsAsync(guild, mentionedNames.Keys);

        MessageInfo? referencedMessage = null;
        if (eventArgs.Message.ReferencedMessage is not null)
        {
            referencedMessage = new MessageInfo
            {
                Id = eventArgs.Message.ReferencedMessage.Id.ToString(),
                Content = eventArgs.Message.ReferencedMessage.Content,
                Timestamp = eventArgs.Message.ReferencedMessage.Timestamp
            };
        }

        return new MessagePayload
        {
            Source = new SourceInfo
            {
                Type = guild is not null ? "guild" : "dm",
                Guild = guild is not null
                    ? new GuildInfo { Id = guild.Id.ToString(), Name = guild.Name }
                    : null,
                Channel = new ChannelInfo
                {
                    Id = eventArgs.Channel.Id.ToString(),
                    Name = eventArgs.Channel.Name
                }
            },
            Author = new UserInfo
            {
                Id = eventArgs.Author.Id.ToString(),
                DisplayName = authorName,
                VoiceChannel = authorVoiceChannel
            },
            Message = new MessageInfo
            {
                Id = eventArgs.Message.Id.ToString(),
                Content = eventArgs.Message.Content,
                Timestamp = eventArgs.Message.Timestamp
            },
            ReferencedMessage = referencedMessage,
            MentionedUsers = mentionedNames
                .Select(kvp => new UserInfo
                {
                    Id = kvp.Key.ToString(),
                    DisplayName = kvp.Value,
                    VoiceChannel = mentionedVoiceChannels.GetValueOrDefault(kvp.Key)
                })
                .ToList()
        };
    }

    private async Task<ChannelInfo?> ResolveVoiceChannelAsync(DiscordGuild? guild, ulong userId)
    {
        if (guild is null || !guild.VoiceStates.TryGetValue(userId, out DiscordVoiceState? voiceState))
            return null;

        DiscordChannel? channel = await voiceState.GetChannelAsync();
        if (channel is null)
            return null;

        return new ChannelInfo
        {
            Id = channel.Id.ToString(),
            Name = channel.Name
        };
    }

    private async Task<Dictionary<ulong, ChannelInfo?>> ResolveManyVoiceChannelsAsync(
        DiscordGuild? guild,
        IEnumerable<ulong> userIds)
    {
        var results = await Task.WhenAll(
            userIds.Select(async id => (Id: id, Channel: await ResolveVoiceChannelAsync(guild, id)))
        );

        return results.ToDictionary(r => r.Id, r => r.Channel);
    }
}