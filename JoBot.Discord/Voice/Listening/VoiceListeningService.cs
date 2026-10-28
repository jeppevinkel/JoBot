using System.Collections.Concurrent;
using System.Threading.Channels;
using DSharpPlus;
using DSharpPlus.Entities;
using JoBot.Core.Actions;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Builders;
using JoBot.Discord.Helpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.Discord.Voice.Listening;

/// <summary>
/// Transcribes what people say in voice channels and passes it to the AI when they address the bot by name,
/// or carry on talking to it shortly after it answered them.
/// </summary>
/// <remarks>
/// Utterances are transcribed one at a time, in the order they were spoken. Answering runs in the background,
/// so a slow AI reply doesn't hold up transcription; the AI service already handles one message per guild at a time.
/// </remarks>
public class VoiceListeningService : BackgroundService, IUtteranceSink
{
    /// <summary>Reply the AI gives to a follow-up that wasn't meant for it.</summary>
    private const string IgnoreReply = "[ignore]";

    // Utterances are dropped rather than queued up while transcription can't keep up
    private const int MaxQueuedUtterances = 32;

    private readonly ILogger<VoiceListeningService> _logger;
    private readonly DiscordClient _discordClient;
    private readonly ITranscriptionService _transcription;
    private readonly IAiService _aiService;
    private readonly ITtsService _ttsService;
    private readonly IVoiceService _voiceService;
    private readonly IMessagePayloadBuilder _payloadBuilder;
    private readonly VoiceListeningOptions _options;
    private readonly TimeProvider _timeProvider;

    private readonly Channel<Utterance> _utterances = Channel.CreateBounded<Utterance>(
        new BoundedChannelOptions(MaxQueuedUtterances)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    private readonly ConcurrentDictionary<ulong, bool> _isBot = new();
    private readonly ConcurrentDictionary<ulong, byte> _pendingBotLookups = new();
    private readonly ConcurrentDictionary<(ulong GuildId, ulong UserId), DateTimeOffset> _lastAnswered = new();

    public VoiceListeningService(
        ILogger<VoiceListeningService> logger,
        DiscordClient discordClient,
        ITranscriptionService transcription,
        IAiService aiService,
        ITtsService ttsService,
        IVoiceService voiceService,
        IMessagePayloadBuilder payloadBuilder,
        IOptions<VoiceListeningOptions> options,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _discordClient = discordClient;
        _transcription = transcription;
        _aiService = aiService;
        _ttsService = ttsService;
        _voiceService = voiceService;
        _payloadBuilder = payloadBuilder;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool ShouldListenTo(ulong guildId, ulong userId)
    {
        if (_isBot.TryGetValue(userId, out var isBot))
            return !isBot;

        if (_discordClient.Guilds.TryGetValue(guildId, out DiscordGuild? guild) &&
            guild.Members.TryGetValue(userId, out DiscordMember? member))
            return !(_isBot[userId] = member.IsBot);

        // Not cached; look it up in the background and skip this user's audio until we know.
        // Better to miss the first moment of a person speaking than to transcribe another bot's music.
        if (_pendingBotLookups.TryAdd(userId, 0))
            _ = LookUpIsBotAsync(userId);

        return false;
    }

    public void Submit(Utterance utterance)
    {
        if (!_utterances.Writer.TryWrite(utterance))
            _logger.LogDebug("Dropped an utterance from user {UserId}", utterance.UserId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Utterance utterance in _utterances.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleUtteranceAsync(utterance, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to handle speech from user {UserId} in guild {GuildId}",
                    utterance.UserId, utterance.GuildId);
            }
        }
    }

    private async Task HandleUtteranceAsync(Utterance utterance, CancellationToken stoppingToken)
    {
        if (!_discordClient.Guilds.TryGetValue(utterance.GuildId, out DiscordGuild? guild) ||
            !guild.Channels.TryGetValue(utterance.ChannelId, out DiscordChannel? channel))
            return;

        var wakeWords = GetWakeWords(guild);
        var transcript = await _transcription.TranscribeAsync(
            utterance.Samples, utterance.SampleRate, wakeWords, stoppingToken);

        if (string.IsNullOrWhiteSpace(transcript))
            return;

        _logger.LogDebug("Heard user {UserId} in guild {GuildId} ({Duration:0.0}s): {Transcript}",
            utterance.UserId, utterance.GuildId, utterance.Duration.TotalSeconds, transcript);

        // Primed with the bot's name, speech-to-text tends to turn background noise into just that name.
        // Left alone, those would count as follow-ups to each other and keep the bot answering nothing.
        if (WakeWordMatcher.IsOnlyWakeWords(transcript, wakeWords))
        {
            _logger.LogDebug("Ignoring transcript from user {UserId} that is only the bot's name", utterance.UserId);
            return;
        }

        var addressedByName = WakeWordMatcher.ContainsWakeWord(transcript, wakeWords);
        if (!addressedByName && !IsFollowUp(utterance.GuildId, utterance.UserId))
            return;

        _logger.LogInformation("User {UserId} spoke to the bot in guild {GuildId}: {Transcript}",
            utterance.UserId, utterance.GuildId, transcript);

        _ = Task.Run(() => AnswerAsync(guild, channel, utterance.UserId, transcript, addressedByName, stoppingToken), stoppingToken);
    }

    private async Task AnswerAsync(
        DiscordGuild guild,
        DiscordChannel channel,
        ulong userId,
        string transcript,
        bool addressedByName,
        CancellationToken stoppingToken)
    {
        // If the AI decides to leave while answering, let the answer be spoken first
        using IDisposable leaveHold = _voiceService.DeferLeaving(guild.Id);

        try
        {
            DiscordUser user = await _discordClient.GetUserAsync(userId);
            if (user.IsBot)
                return;

            if (_options.PostTranscripts)
            {
                var name = guild.Members.TryGetValue(userId, out DiscordMember? member) ? member.DisplayName : user.Username;
                await SendTextAsync(channel, $"🎙️ **{name}:** {transcript}");
            }

            MessagePayload payload = await _payloadBuilder.BuildFromTranscriptAsync(
                guild, channel, user, transcript, new SpokenInputInfo
                {
                    AddressedByName = addressedByName,
                    Note = BuildNote(addressedByName)
                });

            await foreach (AiAction action in _aiService.ProcessAsync(guild.Id, payload, stoppingToken))
            {
                switch (action)
                {
                    case RespondAction { Content: var content } when IsIgnoreReply(content):
                        _logger.LogDebug("AI decided a follow-up from user {UserId} wasn't meant for it", userId);
                        break;
                    case RespondAction response:
                        RecordAnswered(guild.Id, userId, addressedByName, response.IsShared);
                        await DeliverAsync(guild.Id, channel, response.Content, stoppingToken);
                        break;
                    case ReplyAction reply:
                        // Errors from the AI service; not worth speaking out loud
                        await SendTextAsync(channel, reply.Content);
                        break;
                    case MergedAction:
                        // The answer someone else was getting covered this too, and was spoken by their handler
                        RecordAnswered(guild.Id, userId, addressedByName, isShared: true);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to answer user {UserId} in guild {GuildId}", userId, guild.Id);
        }
    }

    private async Task DeliverAsync(ulong guildId, DiscordChannel channel, string content, CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        if (_options.PostReplies)
            await SendTextAsync(channel, content);

        if (!_options.SpeakReplies || !await _voiceService.IsConnectedAsync(guildId))
            return;

        try
        {
            var url = await _ttsService.GenerateAsync(content, stoppingToken);
            await _voiceService.PlayTtsAsync(guildId, url);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to speak reply in guild {GuildId}", guildId);
        }
    }

    private async Task SendTextAsync(DiscordChannel channel, string content)
    {
        try
        {
            foreach (var chunk in MessageChunker.Split(content))
            {
                if (!string.IsNullOrEmpty(chunk))
                    await channel.SendMessageAsync(chunk);
            }
        }
        catch (Exception ex)
        {
            // Most likely missing permission to post in the voice channel's chat
            _logger.LogWarning(ex, "Failed to post in voice channel {ChannelId}", channel.Id);
        }
    }

    private string BuildNote(bool addressedByName)
    {
        var note = "This was said out loud in the voice channel and transcribed automatically, so expect " +
                   "transcription mistakes, especially in names.";

        if (_options.SpeakReplies)
            note += " Your reply is read aloud automatically: keep it short and conversational, without markdown, " +
                    "links or emoji, and don't use the text-to-speech tools for it.";

        if (!addressedByName)
            note += " The speaker didn't say your name, but spoke shortly after you answered them. If this clearly " +
                    $"isn't meant for you, reply with exactly {IgnoreReply} and nothing else.";

        return note;
    }

    private static bool IsIgnoreReply(string content) =>
        content.Trim().Equals(IgnoreReply, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lets the speaker carry on talking to the bot without its name for a while.</summary>
    /// <remarks>
    /// A reply shared with others may have left out a follow-up that wasn't meant for the bot, and there's no telling
    /// whether it did. Only people who said the bot's name get a fresh window then; otherwise someone chatting
    /// near the bot could keep being sent to the AI.
    /// </remarks>
    private void RecordAnswered(ulong guildId, ulong userId, bool addressedByName, bool isShared)
    {
        if (addressedByName || !isShared)
            _lastAnswered[(guildId, userId)] = _timeProvider.GetUtcNow();
    }

    private bool IsFollowUp(ulong guildId, ulong userId) =>
        _options.FollowUpWindow > TimeSpan.Zero &&
        _lastAnswered.TryGetValue((guildId, userId), out DateTimeOffset answeredAt) &&
        _timeProvider.GetUtcNow() - answeredAt <= _options.FollowUpWindow;

    private List<string> GetWakeWords(DiscordGuild guild)
    {
        var wakeWords = new List<string>(_options.WakeWords);

        if (guild.CurrentMember is { } bot)
        {
            wakeWords.Add(bot.DisplayName);
            wakeWords.Add(bot.Username);
        }

        return wakeWords
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task LookUpIsBotAsync(ulong userId)
    {
        try
        {
            DiscordUser user = await _discordClient.GetUserAsync(userId);
            _isBot[userId] = user.IsBot;
        }
        catch (Exception ex)
        {
            // If we can't tell, assume a person rather than retrying for every packet they send
            _logger.LogDebug(ex, "Could not look up voice user {UserId}", userId);
            _isBot[userId] = false;
        }
        finally
        {
            _pendingBotLookups.TryRemove(userId, out _);
        }
    }
}
