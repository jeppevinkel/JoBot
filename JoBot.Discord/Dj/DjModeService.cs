using DSharpPlus;
using DSharpPlus.Entities;
using JoBot.Core.Actions;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Helpers;
using JoBot.Discord.Voice;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.Discord.Dj;

/// <summary>
/// Keeps the queue going in guilds with DJ mode on: when it runs low, asks the AI to add more tracks that fit
/// the session.
/// </summary>
/// <remarks>
/// <para>
/// The AI is asked through <see cref="IAiService.ProcessEventAsync"/>, so it sees the conversation for context
/// without the requests crowding it out. Tracks it queues meanwhile are marked as DJ picks (see <see cref="DjTurn"/>),
/// and tracks people ask for go in front of them.
/// </para>
/// <para>
/// DJ mode turns off when the bot leaves voice, and doesn't ask for tracks while no one is listening.
/// </para>
/// </remarks>
public sealed class DjModeService : BackgroundService
{
    private const string NudgeEvent = "dj_mode_queue_low";

    private const string PausedNotice =
        "DJ mode is paused: I couldn't find anything to add to the queue. Ask me to turn DJ mode back on to try again.";

    private readonly ILogger<DjModeService> _logger;
    private readonly DiscordClient _discordClient;
    private readonly IAiService _aiService;
    private readonly IVoiceService _voiceService;
    private readonly ITtsService _ttsService;
    private readonly VoiceCompany _voiceCompany;
    private readonly DjSessionStore _sessions;
    private readonly DjModeOptions _options;
    private readonly TimeProvider _timeProvider;

    public DjModeService(
        ILogger<DjModeService> logger,
        DiscordClient discordClient,
        IAiService aiService,
        IVoiceService voiceService,
        ITtsService ttsService,
        VoiceCompany voiceCompany,
        DjSessionStore sessions,
        IOptions<DjModeOptions> options,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _discordClient = discordClient;
        _aiService = aiService;
        _voiceService = voiceService;
        _ttsService = ttsService;
        _voiceCompany = voiceCompany;
        _sessions = sessions;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.CheckInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var (guildId, session) in _sessions.All)
            {
                try
                {
                    await CheckAsync(guildId, session, stoppingToken);
                }
                catch (Exception ex)
                {
                    // One guild failing must not stop checks for the others
                    _logger.LogWarning(ex, "Failed to check DJ mode in guild {GuildId}", guildId);
                }
            }
        }
    }

    private async Task CheckAsync(ulong guildId, DjSession session, CancellationToken stoppingToken)
    {
        if (!await _voiceService.IsConnectedAsync(guildId))
        {
            if (_sessions.Disable(guildId, session))
                _logger.LogInformation("Turned DJ mode off in guild {GuildId} because the bot left voice", guildId);
            return;
        }

        QueueInfo? queue = await _voiceService.GetQueueAsync(guildId);
        if (queue is null)
            return;

        session.RecordPlaying(queue.CurrentTrack);

        if (queue.QueuedTracks.Count > _options.LowQueueThreshold)
            return;

        // Don't spend tokens on music no one hears
        if (!await HasListenersAsync(guildId))
            return;

        if (!session.TryBeginNudge(queue.QueuedTracks.Count, _timeProvider.GetUtcNow(), _options))
            return;

        // Runs in the background so a slow AI reply doesn't hold up other guilds
        _ = Task.Run(() => NudgeAsync(guildId, session, stoppingToken), stoppingToken);
    }

    private async Task<bool> HasListenersAsync(ulong guildId)
    {
        if (_discordClient.CurrentUser is not { } currentUser ||
            !_discordClient.Guilds.TryGetValue(guildId, out DiscordGuild? guild) ||
            !guild.VoiceStates.TryGetValue(currentUser.Id, out DiscordVoiceState? botState) ||
            botState.ChannelId is not { } channelId)
            return false;

        return await _voiceCompany.HasCompanyAsync(guild, channelId);
    }

    private async Task NudgeAsync(ulong guildId, DjSession session, CancellationToken stoppingToken)
    {
        // Tracks queued from here on, through the AI service and its tools, are DJ picks
        using DjTurn turn = DjTurn.Begin();

        DjNudgeContext? context = null;
        string? reply = null;

        try
        {
            await foreach (AiAction action in _aiService.ProcessEventAsync(
                               guildId,
                               async _ =>
                               {
                                   (AutomatedEventPayload? payload, context) = await BuildNudgeAsync(guildId, session);
                                   return payload;
                               },
                               stoppingToken))
            {
                switch (action)
                {
                    case RespondAction { Content: var content } when !string.IsNullOrWhiteSpace(content):
                        // Text alongside tool calls is the AI thinking out loud; only its last reply is an announcement
                        reply = content;
                        break;
                    case ReplyAction { Content: var error }:
                        _logger.LogWarning("The AI failed to top up the DJ mode queue in guild {GuildId}: {Error}", guildId, error);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to top up the DJ mode queue in guild {GuildId}", guildId);
        }

        if (context is null)
        {
            // Nothing to do by the time it was the guild's turn
            session.CancelNudge();
            return;
        }

        DjTopUpOutcome outcome = session.CompleteNudge(turn.TracksAdded, _options.MaxRetries);

        // Turned off while the AI was working; stay quiet
        if (!_sessions.IsCurrent(guildId, session))
            return;

        switch (outcome)
        {
            case DjTopUpOutcome.ToppedUp:
                _logger.LogInformation("DJ mode added {Count} track(s) in guild {GuildId}", turn.TracksAdded, guildId);
                if (context.Announce && reply is not null)
                    await AnnounceAsync(guildId, context.ChannelId, reply, stoppingToken);
                break;
            case DjTopUpOutcome.Failed:
                _logger.LogWarning("DJ mode added no tracks in guild {GuildId} ({Failures} of {MaxRetries} attempts)",
                    guildId, session.FailedTopUps, _options.MaxRetries);
                break;
            case DjTopUpOutcome.Paused:
                _logger.LogWarning("Paused DJ mode in guild {GuildId} after {MaxRetries} top-ups added no tracks",
                    guildId, _options.MaxRetries);
                // Always said out loud: otherwise the music just stops, and listeners may not be watching the chat
                await AnnounceAsync(guildId, context.ChannelId, PausedNotice, stoppingToken);
                break;
        }
    }

    /// <summary>Builds the request for more tracks, or returns null if it's no longer needed.</summary>
    private async Task<(AutomatedEventPayload? Payload, DjNudgeContext? Context)> BuildNudgeAsync(
        ulong guildId,
        DjSession session)
    {
        // Waiting for the AI to finish with the guild may have taken a while; check again
        if (!_sessions.IsCurrent(guildId, session) ||
            !_discordClient.Guilds.TryGetValue(guildId, out DiscordGuild? guild))
            return (null, null);

        QueueInfo? queue = await _voiceService.GetQueueAsync(guildId);
        if (queue is null || queue.QueuedTracks.Count > _options.LowQueueThreshold)
            return (null, null);

        DjNudgeContext context = session.MarkNudgeSent(_timeProvider.GetUtcNow());
        var channelName = guild.Channels.TryGetValue(context.ChannelId, out DiscordChannel? channel)
            ? channel.Name
            : "unknown";

        var payload = new AutomatedEventPayload
        {
            Source = new SourceInfo
            {
                Type = "automated",
                Guild = new GuildInfo { Id = guild.Id.ToString(), Name = guild.Name },
                Channel = new ChannelInfo { Id = context.ChannelId.ToString(), Name = channelName }
            },
            Event = NudgeEvent,
            Note = BuildNote(queue.QueuedTracks.Count, context.Announce),
            Details = new DjNudgeDetails
            {
                RemainingTracks = queue.QueuedTracks.Count,
                CurrentTrack = queue.CurrentTrack is null ? null : Describe(queue.CurrentTrack),
                QueuedTracks = queue.QueuedTracks.Select(Describe).ToList(),
                TracksToAdd = _options.TopUpCount,
                Guidance = context.Guidance,
                RecentlyPlayed = context.RecentlyPlayed.Select(Describe).ToList(),
                SkippedTracks = context.Skipped.Select(Describe).ToList()
            }
        };

        return (payload, context);
    }

    private string BuildNote(int remainingTracks, bool announce)
    {
        var note = $"[DJ MODE] This was sent automatically, not by a person. DJ mode is on and the queue is running " +
                   $"low ({remainingTracks} track(s) left after the current one). Add about {_options.TopUpCount} " +
                   "more tracks that fit the session so far: search with the music tools and add them with enqueue. " +
                   "Follow the guidance if there is any, and take what people asked for earlier in the conversation " +
                   "into account. Don't repeat recently played or queued tracks, and steer away from what people " +
                   "skipped. There's no one to ask questions, so pick the tracks yourself.";

        note += announce
            ? " Your final reply is read aloud in the voice channel and posted in chat, like a radio DJ introducing " +
              "what's coming up: one or two short sentences, without markdown, links or emoji, and don't use the " +
              "text-to-speech tools for it."
            : " No one sees your final reply, so keep it to a few words.";

        return note;
    }

    private static string Describe(TrackInfo track) => $"{track.Artist} - {track.Title}";

    /// <summary>Posts <paramref name="text"/> in the channel and speaks it in the voice channel.</summary>
    private async Task AnnounceAsync(ulong guildId, ulong channelId, string text, CancellationToken stoppingToken)
    {
        if (_discordClient.Guilds.TryGetValue(guildId, out DiscordGuild? guild) &&
            guild.Channels.TryGetValue(channelId, out DiscordChannel? channel))
        {
            try
            {
                foreach (var chunk in MessageChunker.Split(text))
                {
                    if (!string.IsNullOrEmpty(chunk))
                        await channel.SendMessageAsync(chunk);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to post DJ mode announcement in channel {ChannelId}", channelId);
            }
        }

        if (!await _voiceService.IsConnectedAsync(guildId))
            return;

        try
        {
            var url = await _ttsService.GenerateAsync(text, stoppingToken);
            await _voiceService.PlayTtsAsync(guildId, url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to speak DJ mode announcement in guild {GuildId}", guildId);
        }
    }
}
