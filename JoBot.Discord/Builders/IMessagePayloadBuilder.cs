using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using JoBot.Core.Models;

namespace JoBot.Discord.Builders;

public interface IMessagePayloadBuilder
{
    Task<MessagePayload> BuildAsync(MessageCreatedEventArgs eventArgs);

    /// <summary>Builds a payload for something said in a voice channel.</summary>
    Task<MessagePayload> BuildFromTranscriptAsync(
        DiscordGuild guild,
        DiscordChannel voiceChannel,
        DiscordUser author,
        string transcript,
        SpokenInputInfo spokenInput);
}