using System.Buffers;
using System.Runtime.InteropServices;
using DSharpPlus.Voice;
using JoBot.Core.Models;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Voice.Native;

/// <summary>
/// Plays a music queue and TTS clips into a single <see cref="VoiceConnection"/>.
/// </summary>
/// <remarks>
/// <para>
/// A single mixer loop owns every decoder. Commands only change state under <see cref="_sync"/> and wake
/// the loop, which applies them at the next 20 ms frame boundary, so a skip or seek can never race with
/// a frame being read.
/// </para>
/// <para>
/// TTS is mixed over the music rather than interrupting it: the music is ducked while a clip plays and
/// carries on underneath, so there is no interrupted track to restore afterwards.
/// </para>
/// </remarks>
public sealed class NativeGuildPlayer : IAsyncDisposable
{
    private const int SampleRate = FfmpegAudioSource.SampleRate;
    private const int FrameSize = FfmpegAudioSource.FrameSize;
    private const int SamplesPerFrame = FfmpegAudioSource.SamplesPerFrame;

    // DSharpPlus.Voice buffers without limit, so stay only ~200 ms ahead of what has actually been sent.
    // That keeps skip, pause, volume and TTS responsive.
    private const int TargetQueuedFrames = 10;
    private static readonly TimeSpan CapacityPollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan BufferingWait = TimeSpan.FromMilliseconds(20);

    private const float DuckedMusicGain = 0.3f;
    private const float TtsGain = 1.0f;

    // Brings every TTS clip to the same loudness (EBU R128, speech-level target), since ElevenLabs output
    // varies between clips and voice models. Peaks are capped at -1.5 dBTP before mixing.
    private const string TtsLoudnessFilter = "loudnorm=I=-16:TP=-1.5:LRA=11";
    // Maximum gain change per frame, so ducking and volume changes fade over ~150 ms instead of clicking
    private const float MaxGainStepPerFrame = 0.15f;

    // Paused decoders hold an open connection; after this long, restart from the saved position instead
    private static readonly TimeSpan MaxPausedSourceAge = TimeSpan.FromMinutes(2);

    private readonly VoiceConnection _connection;
    private readonly string _ffmpegPath;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Task _loopTask;

    private readonly object _sync = new();
    private readonly List<NativeTrack> _queue = [];
    private readonly Queue<string> _ttsQueue = new();
    private NativeTrack? _current;
    private long _positionSamples;
    private int _trackVersion;
    private bool _paused;
    private DateTimeOffset _pausedAt;
    private float _volume;
    private bool _playingTts;
    private bool _stopTtsRequested;
    private int _disposed;

    public NativeGuildPlayer(VoiceConnection connection, float volume, string ffmpegPath, ILogger logger)
    {
        _connection = connection;
        _volume = Math.Clamp(volume, 0f, 1f);
        _ffmpegPath = ffmpegPath;
        _logger = logger;
        _loopTask = Task.Run(RunAsync);
    }

    public ulong ChannelId => _connection.ChannelId;

    /// <summary>False once the playback loop has stopped, e.g. after an unrecoverable error.</summary>
    public bool IsAlive => !_loopTask.IsCompleted;

    public bool IsPlaying
    {
        get
        {
            lock (_sync)
                return (_current is not null && !_paused) || _playingTts;
        }
    }

    public float Volume
    {
        get
        {
            lock (_sync)
                return _volume;
        }
    }

    /// <summary>Replaces the current track, keeping the rest of the queue.</summary>
    public void Play(NativeTrack track)
    {
        lock (_sync)
        {
            SetCurrentLocked(track);
            _paused = false;
        }

        Wake();
    }

    /// <summary>Starts the track if nothing is playing, otherwise adds it to the end of the queue.</summary>
    public void Enqueue(NativeTrack track)
    {
        lock (_sync)
        {
            if (_current is null)
                SetCurrentLocked(track);
            else
                _queue.Add(track);
        }

        Wake();
    }

    public void PlayTts(string url)
    {
        lock (_sync)
            _ttsQueue.Enqueue(url);

        Wake();
    }

    public void Skip()
    {
        lock (_sync)
        {
            AdvanceLocked();
            // Skipping implies the user wants to hear the next track
            _paused = false;
        }

        Wake();
    }

    public void Stop()
    {
        lock (_sync)
        {
            _queue.Clear();
            SetCurrentLocked(null);
            _paused = false;
            _ttsQueue.Clear();
            _stopTtsRequested = true;
        }

        Wake();
    }

    public PlaybackResult Pause()
    {
        lock (_sync)
        {
            if (_current is null)
                return PlaybackResult.NothingPlaying;

            if (!_paused)
            {
                _paused = true;
                _pausedAt = DateTimeOffset.UtcNow;
            }
        }

        Wake();
        return PlaybackResult.Success;
    }

    public PlaybackResult Resume()
    {
        lock (_sync)
        {
            if (_current is null)
                return PlaybackResult.NothingPlaying;

            if (_paused)
            {
                _paused = false;
                if (DateTimeOffset.UtcNow - _pausedAt > MaxPausedSourceAge)
                    _trackVersion++; // Restart the decoder from the saved position
            }
        }

        Wake();
        return PlaybackResult.Success;
    }

    public PlaybackResult Seek(TimeSpan position)
    {
        lock (_sync)
        {
            if (_current is null)
                return PlaybackResult.NothingPlaying;

            if (!_current.IsSeekable)
                return PlaybackResult.NotSeekable;

            if (position < TimeSpan.Zero || position > _current.Duration)
                return PlaybackResult.InvalidArgument;

            _positionSamples = (long)(position.TotalSeconds * SampleRate);
            _trackVersion++;
        }

        Wake();
        return PlaybackResult.Success;
    }

    public void SetVolume(float volume)
    {
        lock (_sync)
            _volume = Math.Clamp(volume, 0f, 1f);
    }

    public NowPlayingInfo GetNowPlaying()
    {
        lock (_sync)
        {
            return new NowPlayingInfo
            {
                Track = _current is null ? null : ToTrackInfo(_current),
                Position = _current is null ? null : TimeSpan.FromSeconds(_positionSamples / SampleRate),
                IsPaused = _current is not null && _paused,
                IsPlayingTts = _playingTts
            };
        }
    }

    public QueueInfo GetQueue()
    {
        lock (_sync)
        {
            return new QueueInfo
            {
                CurrentTrack = _current is null ? null : ToTrackInfo(_current),
                QueuedTracks = _queue.Select((track, index) => new QueuedTrackInfo
                {
                    Position = index + 1,
                    Title = track.Title,
                    Artist = track.Artist,
                    Album = track.Album,
                    Duration = track.Duration ?? TimeSpan.Zero
                }).ToList()
            };
        }
    }

    /// <param name="index">0-based queue index.</param>
    public TrackInfo? RemoveFromQueue(int index)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _queue.Count)
                return null;

            NativeTrack track = _queue[index];
            _queue.RemoveAt(index);
            return ToTrackInfo(track);
        }
    }

    /// <param name="fromIndex">0-based queue index of the track to move.</param>
    /// <param name="toIndex">0-based target index; clamped to the end of the queue.</param>
    public TrackInfo? MoveQueueTrack(int fromIndex, int toIndex)
    {
        lock (_sync)
        {
            if (fromIndex < 0 || fromIndex >= _queue.Count || toIndex < 0)
                return null;

            NativeTrack track = _queue[fromIndex];
            _queue.RemoveAt(fromIndex);
            _queue.Insert(Math.Min(toIndex, _queue.Count), track);
            return ToTrackInfo(track);
        }
    }

    public int ClearQueue()
    {
        lock (_sync)
        {
            var count = _queue.Count;
            _queue.Clear();
            return count;
        }
    }

    private async Task RunAsync()
    {
        CancellationToken ct = _cts.Token;
        AudioWriter? writer = null;
        FfmpegAudioSource? music = null;
        FfmpegAudioSource? tts = null;
        var musicVersion = -1;

        var musicFrame = new byte[FrameSize];
        var ttsFrame = new byte[FrameSize];
        var mixBuffer = new float[FrameSize / sizeof(short)];
        var mixFrame = new byte[FrameSize];
        var limiter = new PeakLimiter();

        float musicGain;
        lock (_sync)
            musicGain = _volume;

        var transmitting = false;
        long framesWritten = 0;
        long sentBaseline = 0;

        try
        {
            writer = _connection.CreateAudioWriter(AudioFormat.S16LE48KHzStereoPCM);

            while (!ct.IsCancellationRequested)
            {
                if (transmitting)
                    sentBaseline = await WaitForSendCapacityAsync(framesWritten, sentBaseline, ct);

                NativeTrack? track;
                long startSamples;
                bool paused;
                int version;
                float volume;
                bool stopTts;

                lock (_sync)
                {
                    track = _current;
                    startSamples = _positionSamples;
                    paused = _paused;
                    version = _trackVersion;
                    volume = _volume;
                    stopTts = _stopTtsRequested;
                    _stopTtsRequested = false;
                }

                // Apply track changes, seeks and stops by replacing the decoder
                if (music is not null && musicVersion != version)
                {
                    await music.DisposeAsync();
                    music = null;
                }

                if (stopTts && tts is not null)
                {
                    await tts.DisposeAsync();
                    tts = null;
                }

                if (music is null && track is not null && !paused)
                {
                    music = TryStartSource(track.Url, TimeSpan.FromSeconds((double)startSamples / SampleRate));
                    musicVersion = version;

                    if (music is null)
                    {
                        // Couldn't even start the decoder (e.g. ffmpeg is missing); move on rather than retrying forever
                        AdvanceIfVersion(version);
                        continue;
                    }
                }

                if (tts is null)
                {
                    string? nextTts;
                    lock (_sync)
                    {
                        _ttsQueue.TryDequeue(out nextTts);
                        _playingTts = nextTts is not null;
                    }

                    if (nextTts is not null)
                        tts = TryStartSource(nextTts, TimeSpan.Zero, TtsLoudnessFilter);
                }

                var haveTts = false;
                if (tts is not null)
                {
                    if (tts.TryReadFrame(ttsFrame))
                    {
                        haveTts = true;
                    }
                    else if (tts.IsCompleted)
                    {
                        await tts.DisposeAsync();
                        tts = null;
                        continue;
                    }
                }

                var haveMusic = false;
                if (music is not null && !paused)
                {
                    if (music.TryReadFrame(musicFrame))
                    {
                        haveMusic = true;
                        lock (_sync)
                        {
                            if (_trackVersion == musicVersion)
                                _positionSamples += SamplesPerFrame;
                        }
                    }
                    else if (music.IsCompleted)
                    {
                        // Track finished
                        await music.DisposeAsync();
                        music = null;
                        AdvanceIfVersion(musicVersion);
                        continue;
                    }
                }

                if (!haveMusic && !haveTts)
                {
                    var idle = (music is null || paused) && tts is null;
                    if (idle && transmitting)
                    {
                        writer.SignalSilence();
                        transmitting = false;
                    }

                    // Idle: sleep until a command arrives. Buffering: check again shortly.
                    await WaitForWakeAsync(idle ? Timeout.InfiniteTimeSpan : BufferingWait, ct);
                    continue;
                }

                var targetGain = volume * (haveTts ? DuckedMusicGain : 1f);
                var nextGain = StepTowards(musicGain, targetGain, MaxGainStepPerFrame);
                Mix(mixBuffer, haveMusic ? musicFrame : null, haveTts ? ttsFrame : null, musicGain, nextGain);
                limiter.Process(mixBuffer, MemoryMarshal.Cast<byte, short>(mixFrame.AsSpan()));
                musicGain = nextGain;

                if (!transmitting)
                {
                    // The library sends its own silence frames when pausing, so restart the count from here
                    sentBaseline = GetFramesConsumed();
                    framesWritten = 0;
                    transmitting = true;
                }

                writer.Write((ReadOnlySpan<byte>)mixFrame);
                framesWritten++;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice playback loop failed in channel {ChannelId}", _connection.ChannelId);
        }
        finally
        {
            if (music is not null)
                await music.DisposeAsync();
            if (tts is not null)
                await tts.DisposeAsync();

            try
            {
                writer?.SignalCompletion();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error completing audio writer");
            }

            lock (_sync)
                _playingTts = false;
        }
    }

    /// <summary>Waits until fewer than <see cref="TargetQueuedFrames"/> written frames are still waiting to be sent.</summary>
    /// <returns>The (possibly re-based) count of frames consumed when transmission started.</returns>
    private async Task<long> WaitForSendCapacityAsync(long framesWritten, long sentBaseline, CancellationToken ct)
    {
        while (true)
        {
            var consumed = GetFramesConsumed();
            // Re-base if the counter went backwards (metrics reset by a reconnect) or ran ahead of what we
            // wrote (silence frames the library sent itself while we were buffering). Either way the queue
            // has drained; without this, each buffering gap would let us drift further ahead.
            if (consumed < sentBaseline || consumed - sentBaseline > framesWritten)
                sentBaseline = consumed - framesWritten;

            if (framesWritten - (consumed - sentBaseline) < TargetQueuedFrames)
                return sentBaseline;

            await Task.Delay(CapacityPollInterval, ct);
        }
    }

    private long GetFramesConsumed()
    {
        var metrics = _connection.GetVoiceMetrics();
        return (long)metrics.AudioFramesSent + metrics.AudioFramesDropped;
    }

    private FfmpegAudioSource? TryStartSource(string url, TimeSpan startPosition, string? audioFilter = null)
    {
        try
        {
            return FfmpegAudioSource.Start(_ffmpegPath, url, startPosition, _logger, Wake, audioFilter);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ffmpeg ({FfmpegPath}). Is it installed?", _ffmpegPath);
            return null;
        }
    }

    private void AdvanceIfVersion(int version)
    {
        lock (_sync)
        {
            // A command may have changed the track in the meantime; don't skip that one
            if (_trackVersion == version)
                AdvanceLocked();
        }
    }

    private void AdvanceLocked() =>
        SetCurrentLocked(_queue.Count > 0 ? _queue[0] : null, removeFirstQueued: _queue.Count > 0);

    private void SetCurrentLocked(NativeTrack? track, bool removeFirstQueued = false)
    {
        if (removeFirstQueued)
            _queue.RemoveAt(0);

        _current = track;
        _positionSamples = 0;
        _trackVersion++;
    }

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
                _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled
        }
        catch (ObjectDisposedException)
        {
            // Player is shutting down
        }
    }

    private async Task WaitForWakeAsync(TimeSpan timeout, CancellationToken ct) =>
        await _wake.WaitAsync(timeout, ct);

    /// <summary>Mixes the sources into <paramref name="output"/> without clipping; the limiter handles overs.</summary>
    private static void Mix(Span<float> output, byte[]? music, byte[]? tts, float startGain, float endGain)
    {
        ReadOnlySpan<short> musicSamples = music is null ? default : MemoryMarshal.Cast<byte, short>(music);
        ReadOnlySpan<short> ttsSamples = tts is null ? default : MemoryMarshal.Cast<byte, short>(tts);

        var count = output.Length;
        var gainStep = (endGain - startGain) / count;

        for (var i = 0; i < count; i++)
        {
            float value = 0;
            if (music is not null)
                value += musicSamples[i] * (startGain + gainStep * i);
            if (tts is not null)
                value += ttsSamples[i] * TtsGain;

            output[i] = value;
        }
    }

    private static float StepTowards(float current, float target, float maxStep) =>
        Math.Abs(target - current) <= maxStep ? target : current + Math.Sign(target - current) * maxStep;

    private static TrackInfo ToTrackInfo(NativeTrack track) => new()
    {
        Title = track.Title,
        Artist = track.Artist,
        Album = track.Album,
        Duration = track.Duration ?? TimeSpan.Zero
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await _cts.CancelAsync();

        try
        {
            await _loopTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Voice playback loop did not stop in time for channel {ChannelId}", _connection.ChannelId);
        }

        try
        {
            await _connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting from voice channel {ChannelId}", _connection.ChannelId);
        }

        _cts.Dispose();
        _wake.Dispose();
    }
}
