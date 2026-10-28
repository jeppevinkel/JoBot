using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Voice;

/// <summary>Tells whether people are in a voice channel with the bot, from the gateway's voice-state cache.</summary>
/// <remarks>Other bots don't count as company.</remarks>
public sealed class VoiceCompany
{
    private readonly ILogger<VoiceCompany> _logger;
    private readonly DiscordClient _discordClient;

    // Whether a user is a bot never changes, so look it up once
    private readonly ConcurrentDictionary<ulong, bool> _isBot = new();

    public VoiceCompany(ILogger<VoiceCompany> logger, DiscordClient discordClient)
    {
        _logger = logger;
        _discordClient = discordClient;
    }

    /// <summary>Whether anyone other than bots is in <paramref name="channelId"/>.</summary>
    public async Task<bool> HasCompanyAsync(DiscordGuild guild, ulong channelId)
    {
        var botId = _discordClient.CurrentUser?.Id;

        foreach (DiscordVoiceState state in guild.VoiceStates.Values)
        {
            if (state.ChannelId == channelId && state.UserId != botId && !await IsBotAsync(state))
                return true;
        }

        return false;
    }

    private async Task<bool> IsBotAsync(DiscordVoiceState state)
    {
        if (_isBot.TryGetValue(state.UserId, out var isBot))
            return isBot;

        try
        {
            DiscordUser? user = await state.GetUserAsync();
            if (user is null)
                return false; // Unknown; treat as a person and look it up again next time

            return _isBot[state.UserId] = user.IsBot;
        }
        catch (Exception ex)
        {
            // If we can't tell, assume a person is there rather than acting as if the bot were alone
            _logger.LogDebug(ex, "Could not look up voice user {UserId}", state.UserId);
            return false;
        }
    }
}
