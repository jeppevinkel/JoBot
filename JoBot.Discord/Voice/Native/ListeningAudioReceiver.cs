using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
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

    // The library keeps the SSRC -> user ID map private; see ResolveUserId
    private static readonly FieldInfo? SsrcsField =
        typeof(VoiceConnection).GetField("ssrcs", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly IAudioCodec _codec;
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
    private Timer? _flushTimer;
    private volatile bool _attached;
    private volatile bool _disposed;

    public ListeningAudioReceiver(
        IAudioCodec codec,
        ITranscriptionService transcription,
        IUtteranceSink sink,
        IOptions<VoiceListeningOptions> options,
        ILogger<ListeningAudioReceiver> logger)
    {
        _codec = codec;
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

        _guildId = guildId;
        _channelId = connection.ChannelId;
        _flushTimer = new Timer(_ => FlushQuietSpeakers(), null, FlushCheckInterval, FlushCheckInterval);
        _attached = true;
    }

    protected override Task ProcessAudioAsync(ulong speakingUserId, uint sequence, AudioTimestamp timestamp, TimeSpan duration, byte[] audio)
    {
        if (!_attached || _disposed || !_transcription.IsAvailable)
            return Task.CompletedTask;

        if (ResolveUserId(speakingUserId) is not { } userId || !_sink.ShouldListenTo(_guildId, userId))
            return Task.CompletedTask;

        // Pauses are detected from packets stopping, so silence frames carry no information
        if (audio.AsSpan().SequenceEqual(OpusSilenceFrame))
            return Task.CompletedTask;

        Speaker speaker = _speakers.GetOrAdd(userId, _ => new Speaker(_codec.CreateDecoder()));
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
                _logger.LogDebug(ex, "Failed to decode audio from user {UserId}", userId);
                return Task.CompletedTask;
            }

            speaker.LastAudio = now;

            if (speaker.Count >= _maxSamples)
                completed = TakeUtterance(userId, speaker);
        }

        if (completed is not null)
            _sink.Submit(completed);

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

            if (completed is not null)
                _sink.Submit(completed);
        }
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

        foreach (Speaker speaker in _speakers.Values)
        {
            lock (speaker)
                speaker.Dispose();
        }

        _speakers.Clear();
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
