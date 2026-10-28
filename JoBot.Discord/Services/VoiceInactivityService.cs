using DSharpPlus;
using DSharpPlus.Entities;
using JoBot.Core.Interfaces;
using JoBot.Discord.Voice;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.Discord.Services;

/// <summary>
/// Leaves voice channels where the bot has had no human company for <see cref="VoiceBackendOptions.AloneTimeout"/>.
/// </summary>
/// <remarks>
/// Works from the gateway's voice-state cache and goes through <see cref="IVoiceService"/>, so it applies to
/// every voice backend. Other bots don't count as company.
/// </remarks>
public class VoiceInactivityService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly ILogger<VoiceInactivityService> _logger;
    private readonly DiscordClient _discordClient;
    private readonly IVoiceService _voiceService;
    private readonly VoiceCompany _voiceCompany;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private readonly AloneTracker _tracker = new();

    public VoiceInactivityService(
        ILogger<VoiceInactivityService> logger,
        DiscordClient discordClient,
        IVoiceService voiceService,
        VoiceCompany voiceCompany,
        IOptions<VoiceBackendOptions> options,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _discordClient = discordClient;
        _voiceService = voiceService;
        _voiceCompany = voiceCompany;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeout = options.Value.AloneTimeout;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_timeout <= TimeSpan.Zero)
        {
            _logger.LogInformation("Leaving empty voice channels automatically is disabled");
            return;
        }

        using var timer = new PeriodicTimer(CheckInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (DiscordGuild guild in _discordClient.Guilds.Values)
            {
                try
                {
                    await CheckGuildAsync(guild);
                }
                catch (Exception ex)
                {
                    // One guild failing must not stop checks for the others
                    _logger.LogWarning(ex, "Failed to check voice inactivity in guild {GuildId}", guild.Id);
                }
            }
        }
    }

    private async Task CheckGuildAsync(DiscordGuild guild)
    {
        // Before the gateway is ready there is no current user yet
        if (_discordClient.CurrentUser is not { } currentUser)
            return;

        var botId = currentUser.Id;

        if (!guild.VoiceStates.TryGetValue(botId, out DiscordVoiceState? botState) ||
            botState.ChannelId is not { } channelId ||
            !await _voiceService.IsConnectedAsync(guild.Id))
        {
            _tracker.Forget(guild.Id);
            return;
        }

        var hasCompany = await _voiceCompany.HasCompanyAsync(guild, channelId);

        if (!_tracker.Update(guild.Id, isAlone: !hasCompany, _timeProvider.GetUtcNow(), _timeout))
            return;

        _logger.LogInformation(
            "Leaving voice channel {ChannelId} in guild {GuildId} after being alone for {Timeout}",
            channelId, guild.Id, _timeout);

        _tracker.Forget(guild.Id);
        await _voiceService.LeaveVoiceChannelAsync(guild.Id);
    }
}
