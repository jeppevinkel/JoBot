using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Tracks;

namespace JoBot.Discord.Lavalink.Players;

/// <summary>
/// A queued player that can temporarily interrupt music with TTS clips.
/// </summary>
/// <remarks>
/// The <c>*Music*</c> members operate on the music the user is listening to. While a TTS clip is
/// interrupting playback they act on the interrupted track instead of the TTS clip, and the change
/// is applied when the music is restored.
/// </remarks>
public class TtsQueuedPlayer : QueuedLavalinkPlayer
{
    private const float TtsVolumeBoost = 2.0f;

    private readonly Queue<LavalinkTrack> _ttsQueue = new();
    private readonly HashSet<string> _ttsTrackIds = new();
    private ITrackQueueItem? _interruptedItem;
    private TimeSpan? _interruptedPosition;
    private bool _playingTts;
    private float _preInterruptVolume;
    private bool _pauseAfterTts;

    public TtsQueuedPlayer(
        IPlayerProperties<TtsQueuedPlayer, QueuedLavalinkPlayerOptions> properties)
        : base(properties)
    {
    }

    public bool IsPlayingTts => _playingTts;

    /// <summary>The music track currently playing, ignoring any TTS clip that is interrupting it.</summary>
    public LavalinkTrack? CurrentMusicTrack => _playingTts ? _interruptedItem?.Track : CurrentTrack;

    /// <summary>The playback position of <see cref="CurrentMusicTrack"/>.</summary>
    public TimeSpan? MusicPosition
    {
        get
        {
            if (_playingTts)
                return _interruptedItem is null ? null : _interruptedPosition ?? TimeSpan.Zero;

            if (CurrentTrack is null || Position is not { } position)
                return null;

            // The interpolated position keeps advancing while paused, so use the last synced value instead
            TimeSpan value = IsPaused ? position.RelativePosition : position.Position;
            return Clamp(value, TimeSpan.Zero, CurrentTrack.Duration);
        }
    }

    public bool IsMusicPaused => _playingTts ? _pauseAfterTts : IsPaused;

    public float MusicVolume => _playingTts ? _preInterruptVolume : Volume;

    public async Task PlayTtsAsync(
        LavalinkTrack ttsTrack,
        CancellationToken cancellationToken = default)
    {
        _ttsTrackIds.Add(ttsTrack.Identifier);

        if (_playingTts)
        {
            _ttsQueue.Enqueue(ttsTrack);
            return;
        }

        _interruptedItem = CurrentItem;
        _interruptedPosition = MusicPosition;
        _pauseAfterTts = IsPaused;

        await PlayAsync(ttsTrack, enqueue: false, cancellationToken: cancellationToken);

        // Playing a new track does not unpause the player, so TTS would be silent without this
        if (IsPaused)
            await ResumeAsync(cancellationToken);
    }

    public async ValueTask PauseMusicAsync(CancellationToken cancellationToken = default)
    {
        if (_playingTts)
        {
            _pauseAfterTts = true;
            return;
        }

        if (!IsPaused)
            await PauseAsync(cancellationToken);
    }

    public async ValueTask ResumeMusicAsync(CancellationToken cancellationToken = default)
    {
        if (_playingTts)
        {
            _pauseAfterTts = false;
            return;
        }

        if (IsPaused)
            await ResumeAsync(cancellationToken);
    }

    public async ValueTask SeekMusicAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (_playingTts)
        {
            _interruptedPosition = position;
            return;
        }

        await SeekAsync(position, cancellationToken);
    }

    public async ValueTask SetMusicVolumeAsync(float volume, CancellationToken cancellationToken = default)
    {
        if (_playingTts)
        {
            // Keep the TTS boost relative to the new volume; music is restored to the new volume afterwards
            _preInterruptVolume = volume;
            await SetVolumeAsync(Math.Min(1.0f, volume * TtsVolumeBoost), cancellationToken);
            return;
        }

        await SetVolumeAsync(volume, cancellationToken);
    }

    /// <summary>Skips the current music track. During TTS the interrupted track is dropped instead of skipping the TTS clip.</summary>
    public async ValueTask SkipMusicAsync(CancellationToken cancellationToken = default)
    {
        if (_playingTts)
        {
            // The next queued track starts once all TTS has finished
            _interruptedItem = null;
            _interruptedPosition = null;
            _pauseAfterTts = false;
            return;
        }

        await SkipAsync(cancellationToken: cancellationToken);

        // Lavalink keeps the paused flag across tracks; skipping implies the user wants to hear the next one
        if (IsPaused)
            await ResumeAsync(cancellationToken);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        // Drop pending TTS and the interrupted track so nothing is re-queued after stopping.
        // _playingTts is left as-is so the track-ended handler still restores the volume.
        _ttsQueue.Clear();
        _ttsTrackIds.Clear();
        _interruptedItem = null;
        _interruptedPosition = null;
        _pauseAfterTts = false;

        await base.StopAsync(cancellationToken);

        // Lavalink keeps the paused flag after stopping. Left paused, the player would report a Paused
        // state with no track, so newly enqueued tracks would be queued but never started.
        if (IsPaused && State is not PlayerState.Destroyed)
            await ResumeAsync(cancellationToken);
    }

    protected override async ValueTask NotifyTrackEndedAsync(
        ITrackQueueItem queueItem,
        TrackEndReason endReason,
        CancellationToken cancellationToken = default)
    {
        if (_playingTts)
        {
            if (_ttsQueue.TryDequeue(out var nextTts))
            {
                // Insert the next TTS at the front so base plays it next
                await Queue.InsertAsync(0, new TrackQueueItem(nextTts), cancellationToken);
            }
            else
            {
                // All TTS done — restore volume and re-queue interrupted music
                _playingTts = false;
                await SetVolumeAsync(_preInterruptVolume, cancellationToken);

                if (_interruptedItem is not null)
                {
                    await Queue.InsertAsync(0, _interruptedItem, cancellationToken);
                    _interruptedItem = null;
                }
                else
                {
                    _interruptedPosition = null;
                    _pauseAfterTts = false;
                }
            }
        }

        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken);
    }

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStartedAsync(track, cancellationToken);

        if (track.Track is not null && _ttsTrackIds.Remove(track.Track.Identifier))
        {
            if (!_playingTts)
            {
                _playingTts = true;
                _preInterruptVolume = Volume;
                await SetVolumeAsync(Math.Min(1.0f, Volume * TtsVolumeBoost), cancellationToken);
            }
        }
        else if (_interruptedPosition.HasValue)
        {
            if (_interruptedPosition.Value > TimeSpan.Zero && track.Track is { IsSeekable: true })
                await SeekAsync(_interruptedPosition.Value, cancellationToken);
            _interruptedPosition = null;

            if (_pauseAfterTts)
            {
                _pauseAfterTts = false;
                await PauseAsync(cancellationToken);
            }
        }
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) =>
        value < min ? min : value > max ? max : value;

    public static ValueTask<TtsQueuedPlayer> CreateAsync(
        IPlayerProperties<TtsQueuedPlayer, QueuedLavalinkPlayerOptions> properties,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new TtsQueuedPlayer(properties));
    }
}
