using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Voice;
using DSharpPlus.Voice.Codec;
using DSharpPlus.Voice.Receivers;
using JoBot.Core.Interfaces;
using JoBot.Discord.Voice.Listening;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.Discord.Voice.Native;

/// <summary>
/// Decodes the audio of each speaker in a voice channel and cuts it into utterances at pauses, which are handed
/// to an <see cref="IUtteranceSink"/> for transcription.
/// </summary>
/// <remarks>
/// <para>
/// This replaces DSharpPlus.Voice's <see cref="DefaultAudioReceiver"/>, which in the version used here never
/// flushes its pipes (so nothing can be read) and looks speakers up by user ID while being handed RTP SSRCs.
/// </para>
/// <para>
/// Created per connection from the connection's service scope, then <see cref="Attach"/>ed once connected.
/// Audio is only decoded while the transcription server is available, so an idle receiver costs next to nothing.
/// </para>
/// </remarks>
public sealed class ListeningAudioReceiver : AudioReceiver, IDisposable
{
    public const int SampleRate = 48000;
    private const int Channels = 2;

    // Opus' "comfort noise" frame; clients send a few of these when they stop talking
    private static readonly byte[] OpusSilenceFrame = [0xF8, 0xFF, 0xFE];

    // Discord sends nothing between a speaker's sentences. Put a little of that pause back so words on either
    // side don't run together, but don't keep long stretches of silence.
    private static readonly TimeSpan MinGapToFill = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan MaxFilledGap = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FlushCheckInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StatsInterval = TimeSpan.FromMinutes(1);

    // The library keeps the SSRC -> user ID map private; see ResolveUserId
    private static readonly FieldInfo? SsrcsField =
        typeof(VoiceConnection).GetField("ssrcs", BindingFlags.Instance | BindingFlags.NonPublic);

    // The library doesn't observe its receive loop, so if that dies nothing would ever say so
    private static readonly FieldInfo? ReceiveTaskField =
        typeof(VoiceConnection).GetField("receiveAudioTask", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly IAudioCodec _codec;
    private readonly DiscordClient _discordClient;
    private readonly SpeakerIdentifier _speakerIdentifier;
    private readonly ITranscriptionService _transcription;
    private readonly IUtteranceSink _sink;
    private readonly VoiceListeningOptions _options;
    private readonly ILogger<ListeningAudioReceiver> _logger;
    private readonly ConcurrentDictionary<ulong, Speaker> _speakers = new();
    private readonly int _maxSamples;
    private readonly int _minSamples;

    private ConcurrentDictionary<uint, ulong>? _ssrcs;
    private ulong _guildId;
    private ulong _channelId;
    private VoiceConnection? _connection;
    private Timer? _flushTimer;
    private Timer? _statsTimer;
    private ReceiveStats _stats;
    private VoiceMetricsSnapshot _lastMetrics;
    private bool _reportedReceiveFailure;
    private volatile bool _attached;
    private volatile bool _disposed;

    public ListeningAudioReceiver(
        IAudioCodec codec,
        DiscordClient discordClient,
        SpeakerIdentifier speakerIdentifier,
        ITranscriptionService transcription,
        IUtteranceSink sink,
        IOptions<VoiceListeningOptions> options,
        ILogger<ListeningAudioReceiver> logger)
    {
        _codec = codec;
        _discordClient = discordClient;
        _speakerIdentifier = speakerIdentifier;
        _transcription = transcription;
        _sink = sink;
        _options = options.Value;
        _logger = logger;
        _maxSamples = (int)(_options.MaxUtteranceLength.TotalSeconds * SampleRate);
        _minSamples = (int)(_options.MinUtteranceLength.TotalSeconds * SampleRate);
    }

    /// <summary>Starts capturing audio for <paramref name="connection"/>. Audio received before this is ignored.</summary>
    public void Attach(VoiceConnection connection, ulong guildId)
    {
        _ssrcs = SsrcsField?.GetValue(connection) as ConcurrentDictionary<uint, ulong>;
        if (_ssrcs is null)
        {
            _logger.LogWarning(
                "Can't tell who is speaking with this version of DSharpPlus.Voice; voice listening only works " +
                "once the library reports user IDs to audio receivers");
        }

        _connection = connection;
        _guildId = guildId;
        _channelId = connection.ChannelId;

        if (!_speakerIdentifier.Attach(connection, GetPeopleInChannel))
            _logger.LogWarning("Can't identify speakers Discord doesn't announce with this version of DSharpPlus.Voice");

        _lastMetrics = VoiceMetricsSnapshot.From(connection);
        _flushTimer = new Timer(_ => FlushQuietSpeakers(), null, FlushCheckInterval, FlushCheckInterval);
        _statsTimer = new Timer(_ => LogStats(), null, StatsInterval, StatsInterval);
        _attached = true;

        _logger.LogInformation("Listening in voice channel {ChannelId} in guild {GuildId}", _channelId, guildId);
    }

    protected override Task ProcessAudioAsync(ulong speakingUserId, uint sequence, AudioTimestamp timestamp, TimeSpan duration, byte[] audio)
    {
        if (!_attached || _disposed)
            return Task.CompletedTask;

        Interlocked.Increment(ref _stats.Packets);

        if (!_transcription.IsAvailable)
        {
            Interlocked.Increment(ref _stats.TranscriptionUnavailable);
            return Task.CompletedTask;
        }

        if (ResolveUserId(speakingUserId) is not { } userId)
        {
            Interlocked.Increment(ref _stats.UnknownSpeaker);
            return Task.CompletedTask;
        }

        if (!_sink.ShouldListenTo(_guildId, userId))
        {
            Interlocked.Increment(ref _stats.Ignored);
            return Task.CompletedTask;
        }

        // Pauses are detected from packets stopping, so silence frames carry no information
        if (audio.AsSpan().SequenceEqual(OpusSilenceFrame))
            return Task.CompletedTask;

        if (!_speakers.TryGetValue(userId, out Speaker? speaker))
        {
            speaker = _speakers.GetOrAdd(userId, _ => new Speaker(_codec.CreateDecoder()));
            _logger.LogInformation("Receiving audio from user {UserId} in voice channel {ChannelId}", userId, _channelId);
        }

        Utterance? completed = null;

        lock (speaker)
        {
            if (speaker.IsDisposed)
                return Task.CompletedTask;

            var now = Stopwatch.GetTimestamp();
            if (speaker.Count > 0)
            {
                TimeSpan gap = Stopwatch.GetElapsedTime(speaker.LastAudio, now) - duration;
                if (gap >= MinGapToFill)
                    speaker.AppendSilence((int)(Math.Min(gap.TotalSeconds, MaxFilledGap.TotalSeconds) * SampleRate));
            }

            try
            {
                speaker.Decode(audio);
            }
            catch (Exception ex)
            {
                // A corrupt packet shouldn't take down the receive loop
                if (Interlocked.Increment(ref _stats.DecodeFailures) == 1)
                    _logger.LogWarning(ex, "Failed to decode audio from user {UserId}", userId);
                return Task.CompletedTask;
            }

            speaker.LastAudio = now;

            if (speaker.Count >= _maxSamples)
                completed = TakeUtterance(userId, speaker);
        }

        Submit(completed);
        return Task.CompletedTask;
    }

    protected override Task ProcessUserLeftAsync(ulong id)
    {
        if (_speakers.TryRemove(id, out Speaker? speaker))
        {
            lock (speaker)
                speaker.Dispose();
        }

        return Task.CompletedTask;
    }

    protected override Task Initialize(IReadOnlyList<ulong> joinedUsers) => Task.CompletedTask;

    protected override Task ProcessUserJoinedAsync(ulong id) => Task.CompletedTask;

    protected override Task ProcessUserStartedSpeakingAsync(ulong id) => Task.CompletedTask;

    protected override Task ProcessUserStoppedSpeakingAsync(ulong id) => Task.CompletedTask;

    /// <summary>
    /// This version of the library passes the speaker's RTP SSRC where a user ID is documented. Snowflakes never fit
    /// in 32 bits, so anything larger is already a user ID; that keeps this working once the library is fixed.
    /// </summary>
    private ulong? ResolveUserId(ulong id)
    {
        if (id > uint.MaxValue)
            return id;

        return _ssrcs is not null && _ssrcs.TryGetValue((uint)id, out var userId) ? userId : null;
    }

    /// <summary>The users in the channel whose audio is worth listening to.</summary>
    private IReadOnlyCollection<ulong> GetPeopleInChannel()
    {
        if (!_discordClient.Guilds.TryGetValue(_guildId, out DiscordGuild? guild))
            return [];

        var botId = _discordClient.CurrentUser.Id;
        return guild.VoiceStates.Values
            .Where(state => state.ChannelId == _channelId && state.UserId != botId)
            .Select(state => state.UserId)
            .Where(userId => _sink.ShouldListenTo(_guildId, userId))
            .ToList();
    }

    private void FlushQuietSpeakers()
    {
        var now = Stopwatch.GetTimestamp();

        foreach ((var userId, Speaker speaker) in _speakers)
        {
            Utterance? completed = null;

            lock (speaker)
            {
                if (speaker.Count > 0 && !speaker.IsDisposed &&
                    Stopwatch.GetElapsedTime(speaker.LastAudio, now) >= _options.SilenceTimeout)
                    completed = TakeUtterance(userId, speaker);
            }

            Submit(completed);
        }
    }

    private void Submit(Utterance? utterance)
    {
        if (utterance is null)
            return;

        Interlocked.Increment(ref _stats.Utterances);
        _sink.Submit(utterance);
    }

    /// <summary>Logs what happened to received audio, to make it possible to tell where listening breaks down.</summary>
    private void LogStats()
    {
        if (_connection is null || _disposed)
            return;

        if (!_reportedReceiveFailure && ReceiveTaskField?.GetValue(_connection) is Task { IsFaulted: true } receiveTask)
        {
            _reportedReceiveFailure = true;
            _logger.LogError(receiveTask.Exception?.GetBaseException(),
                "The voice library stopped receiving audio in channel {ChannelId}; rejoin the channel to listen again",
                _channelId);
        }

        VoiceMetricsSnapshot metrics;
        try
        {
            metrics = VoiceMetricsSnapshot.From(_connection);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read voice metrics");
            return;
        }

        VoiceMetricsSnapshot delta = metrics - _lastMetrics;
        _lastMetrics = metrics;
        ReceiveStats stats = ReceiveStats.TakeFrom(ref _stats);

        // Stay quiet while nobody talks
        if (delta.FramesReceived == 0 && stats.Packets == 0)
            return;

        _logger.LogInformation(
            "Voice receive in channel {ChannelId} over the last {Interval}: {FramesReceived} frames from Discord, " +
            "{FailedDecryption} failed decryption, {Dropped} dropped (sent or received); " +
            "{Packets} reached the receiver, {Unavailable} skipped while transcription was unavailable, " +
            "{UnknownSpeaker} from unknown speakers, {Ignored} from ignored users (bots), {DecodeFailures} failed to decode; " +
            "{Utterances} utterances sent for transcription",
            _channelId, StatsInterval, delta.FramesReceived, delta.FailedDecryption, delta.Dropped,
            stats.Packets, stats.TranscriptionUnavailable, stats.UnknownSpeaker, stats.Ignored, stats.DecodeFailures,
            stats.Utterances);
    }

    /// <returns>The buffered audio, or null if it's too short to be worth transcribing.</returns>
    private Utterance? TakeUtterance(ulong userId, Speaker speaker)
    {
        var samples = speaker.Take();
        return samples.Length >= _minSamples
            ? new Utterance(_guildId, _channelId, userId, samples, SampleRate)
            : null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _flushTimer?.Dispose();
        _statsTimer?.Dispose();

        foreach (Speaker speaker in _speakers.Values)
        {
            lock (speaker)
                speaker.Dispose();
        }

        _speakers.Clear();
    }

    private struct ReceiveStats
    {
        public int Packets;
        public int TranscriptionUnavailable;
        public int UnknownSpeaker;
        public int Ignored;
        public int DecodeFailures;
        public int Utterances;

        public static ReceiveStats TakeFrom(ref ReceiveStats stats) => new()
        {
            Packets = Interlocked.Exchange(ref stats.Packets, 0),
            TranscriptionUnavailable = Interlocked.Exchange(ref stats.TranscriptionUnavailable, 0),
            UnknownSpeaker = Interlocked.Exchange(ref stats.UnknownSpeaker, 0),
            Ignored = Interlocked.Exchange(ref stats.Ignored, 0),
            DecodeFailures = Interlocked.Exchange(ref stats.DecodeFailures, 0),
            Utterances = Interlocked.Exchange(ref stats.Utterances, 0)
        };
    }

    /// <summary>The receive side of the library's (cumulative) voice metrics.</summary>
    private readonly record struct VoiceMetricsSnapshot(int FramesReceived, int FailedDecryption, int Dropped)
    {
        public static VoiceMetricsSnapshot From(VoiceConnection connection)
        {
            var metrics = connection.GetVoiceMetrics();
            return new VoiceMetricsSnapshot(metrics.AudioFramesReceived, metrics.AudioFramesFailedDecryption, metrics.AudioFramesDropped);
        }

        public static VoiceMetricsSnapshot operator -(VoiceMetricsSnapshot a, VoiceMetricsSnapshot b) =>
            new(a.FramesReceived - b.FramesReceived, a.FailedDecryption - b.FailedDecryption, a.Dropped - b.Dropped);
    }

    /// <summary>One user's decoder and the mono audio buffered since their last pause.</summary>
    private sealed class Speaker(IAudioDecoder decoder) : IDisposable
    {
        private readonly ArrayBufferWriter<short> _decoded = new();
        private short[] _buffer = new short[SampleRate * 5];

        public int Count { get; private set; }
        public long LastAudio { get; set; }
        public bool IsDisposed { get; private set; }

        public void Decode(byte[] opus)
        {
            _decoded.ResetWrittenCount();
            decoder.Decode(opus, _decoded);

            // Down-mix to mono; speech-to-text models don't use stereo
            ReadOnlySpan<short> stereo = _decoded.WrittenSpan;
            var frames = stereo.Length / Channels;
            EnsureCapacity(Count + frames);

            for (var i = 0; i < frames; i++)
                _buffer[Count + i] = (short)((stereo[i * 2] + stereo[i * 2 + 1]) / 2);

            Count += frames;
        }

        public void AppendSilence(int samples)
        {
            EnsureCapacity(Count + samples);
            Array.Clear(_buffer, Count, samples);
            Count += samples;
        }

        public short[] Take()
        {
            var samples = _buffer.AsSpan(0, Count).ToArray();
            Count = 0;
            return samples;
        }

        private void EnsureCapacity(int required)
        {
            if (required > _buffer.Length)
                Array.Resize(ref _buffer, Math.Max(required, _buffer.Length * 2));
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            decoder.Dispose();
        }
    }
}
