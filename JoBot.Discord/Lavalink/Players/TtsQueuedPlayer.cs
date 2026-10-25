using Lavalink4NET.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Tracks;

namespace JoBot.Discord.Lavalink.Players;

/// <summary>
/// A queued player that can temporarily interrupt music with TTS clips.
/// </summary>
/// <remarks>
/// <para>
/// A TTS session starts when the first clip is requested and ends when the last queued clip finishes,
/// at which point the interrupted music is put back at the front of the queue. Clips requested during
/// a session are queued rather than interrupting again, so back-to-back requests can't mistake a TTS
/// clip for the music to restore.
/// </para>
/// <para>
/// The <c>*Music*</c> members operate on the music the user is listening to. During a session they
/// act on the interrupted track instead of the TTS clip, and the change is applied once it is restored.
/// </para>
/// </remarks>
public class TtsQueuedPlayer : QueuedLavalinkPlayer
{
    private const float TtsVolumeBoost = 2.0f;

    // How long past a clip's expected end a session is considered stuck, e.g. because the node
    // reconnected and the track end event was lost
    private static readonly TimeSpan StaleSessionGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UnknownClipDuration = TimeSpan.FromMinutes(5);

    // Guards the TTS state below. Never held across an await.
    private readonly object _sync = new();
    private readonly Queue<LavalinkTrack> _ttsQueue = new();
    private readonly HashSet<string> _ttsTrackIds = new();
    private bool _ttsActive;
    private DateTimeOffset _ttsDeadline;
    private ITrackQueueItem? _interruptedItem;
    private TimeSpan? _interruptedPosition;
    private bool _pauseAfterTts;
    private float _preInterruptVolume;
    private PendingRestore? _pendingRestore;

    public TtsQueuedPlayer(
        IPlayerProperties<TtsQueuedPlayer, QueuedLavalinkPlayerOptions> properties)
        : base(properties)
    {
    }

    public bool IsPlayingTts => _ttsActive;

    /// <summary>The music track currently playing, ignoring any TTS clip that is interrupting it.</summary>
    public LavalinkTrack? CurrentMusicTrack
    {
        get
        {
            lock (_sync)
                return _ttsActive ? _interruptedItem?.Track : CurrentTrack;
        }
    }

    /// <summary>The playback position of <see cref="CurrentMusicTrack"/>.</summary>
    public TimeSpan? MusicPosition
    {
        get
        {
            lock (_sync)
            {
                if (_ttsActive)
                    return _interruptedItem is null ? null : _interruptedPosition ?? TimeSpan.Zero;
            }

            return GetPlaybackPosition();
        }
    }

    public bool IsMusicPaused
    {
        get
        {
            lock (_sync)
                return _ttsActive ? _pauseAfterTts : IsPaused;
        }
    }

    public float MusicVolume
    {
        get
        {
            lock (_sync)
                return _ttsActive ? _preInterruptVolume : Volume;
        }
    }

    public async Task PlayTtsAsync(
        LavalinkTrack ttsTrack,
        CancellationToken cancellationToken = default)
    {
        float baseVolume;

        lock (_sync)
        {
            var staleSession = _ttsActive && DateTimeOffset.UtcNow > _ttsDeadline;
            if (staleSession)
            {
                // The end of the last clip was never reported. Start over instead of queueing behind a clip
                // that will never finish, keeping the music that was originally interrupted.
                _ttsQueue.Clear();
                _ttsTrackIds.Clear();
                _ttsActive = false;
            }

            _ttsTrackIds.Add(ttsTrack.Identifier);

            if (_ttsActive)
            {
                _ttsQueue.Enqueue(ttsTrack);
                return;
            }

            _ttsActive = true;
            _interruptedItem = staleSession ? _interruptedItem : CurrentItem;
            _interruptedPosition = staleSession ? _interruptedPosition : _interruptedItem is null ? null : GetPlaybackPosition();
            _pauseAfterTts = staleSession ? _pauseAfterTts : _interruptedItem is not null && IsPaused;
            _preInterruptVolume = staleSession ? _preInterruptVolume : Volume;
            baseVolume = _preInterruptVolume;
            _ttsDeadline = GetClipDeadline(ttsTrack);
        }

        try
        {
            await PlayAsync(ttsTrack, enqueue: false, cancellationToken: cancellationToken);
        }
        catch
        {
            // The clip never started, so the music is still playing; don't leave a session open
            lock (_sync)
                ClearTtsSession();
            throw;
        }

        // Playing a new track does not unpause the player, so TTS would be silent without this
        if (IsPaused)
            await ResumeAsync(cancellationToken);

        await SetVolumeAsync(BoostVolume(baseVolume), cancellationToken);
    }

    public async ValueTask PauseMusicAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_ttsActive)
            {
                _pauseAfterTts = true;
                return;
            }
        }

        if (!IsPaused)
            await PauseAsync(cancellationToken);
    }

    public async ValueTask ResumeMusicAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_ttsActive)
            {
                _pauseAfterTts = false;
                return;
            }
        }

        if (IsPaused)
            await ResumeAsync(cancellationToken);
    }

    public async ValueTask SeekMusicAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_ttsActive)
            {
                _interruptedPosition = position;
                return;
            }
        }

        await SeekAsync(position, cancellationToken);
    }

    public async ValueTask SetMusicVolumeAsync(float volume, CancellationToken cancellationToken = default)
    {
        var playerVolume = volume;

        lock (_sync)
        {
            if (_ttsActive)
            {
                // Keep the TTS boost relative to the new volume; music is restored to the new volume afterwards
                _preInterruptVolume = volume;
                playerVolume = BoostVolume(volume);
            }
        }

        await SetVolumeAsync(playerVolume, cancellationToken);
    }

    /// <summary>Skips the current music track. During TTS the interrupted track is dropped instead of skipping the TTS clip.</summary>
    public async ValueTask SkipMusicAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_ttsActive)
            {
                // The next queued track starts once all TTS has finished
                _interruptedItem = null;
                _interruptedPosition = null;
                _pauseAfterTts = false;
                return;
            }
        }

        await SkipAsync(cancellationToken: cancellationToken);

        // Lavalink keeps the paused flag across tracks; skipping implies the user wants to hear the next one
        if (IsPaused)
            await ResumeAsync(cancellationToken);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        bool wasActive;
        float volume;

        // End any TTS session up front so nothing is re-queued when the clip reports it was stopped
        lock (_sync)
        {
            wasActive = _ttsActive;
            volume = _preInterruptVolume;
            ClearTtsSession();
            _pendingRestore = null;
        }

        if (wasActive && State is not PlayerState.Destroyed)
            await SetVolumeAsync(volume, cancellationToken);

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
        LavalinkTrack? nextTts = null;
        float? restoreVolume = null;

        lock (_sync)
        {
            // Only the end of a TTS clip moves the session along. In particular, the music ending with
            // reason Replaced when a clip starts must not be mistaken for the clip ending.
            if (_ttsActive && IsTtsItem(queueItem))
            {
                _ttsTrackIds.Remove(queueItem.Track!.Identifier);

                if (endReason.MayStartNext() && _ttsQueue.TryDequeue(out var next))
                    nextTts = next;
                else
                    restoreVolume = _preInterruptVolume;
            }
        }

        ITrackQueueItem? restoreItem = null;

        if (restoreVolume is not null)
        {
            // The session stays open while this awaits, so clips requested meanwhile are queued rather
            // than started and then immediately replaced by the restored music
            await SetVolumeAsync(restoreVolume.Value, cancellationToken);

            float? reboostVolume = null;
            lock (_sync)
            {
                if (endReason.MayStartNext() && _ttsQueue.TryDequeue(out var late))
                {
                    nextTts = late;
                    reboostVolume = BoostVolume(_preInterruptVolume);
                }
                else
                {
                    restoreItem = _interruptedItem;
                    _pendingRestore = restoreItem?.Track is { } track
                        ? new PendingRestore(track.Identifier, _interruptedPosition, _pauseAfterTts)
                        : null;
                    ClearTtsSession();
                }
            }

            if (reboostVolume is not null)
                await SetVolumeAsync(reboostVolume.Value, cancellationToken);
        }

        if (nextTts is not null)
        {
            lock (_sync)
                _ttsDeadline = GetClipDeadline(nextTts);

            // Insert the next TTS at the front so base plays it next
            await Queue.InsertAsync(0, new TrackQueueItem(nextTts), cancellationToken);
        }
        else if (restoreItem is not null)
        {
            await Queue.InsertAsync(0, restoreItem, cancellationToken);
        }

        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken);
    }

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStartedAsync(track, cancellationToken);

        PendingRestore? restore;
        lock (_sync)
        {
            if (IsTtsItem(track))
                return;

            restore = _pendingRestore;
            _pendingRestore = null;
        }

        if (restore is null || track.Track?.Identifier != restore.Identifier)
            return;

        if (restore.Position is { } position && position > TimeSpan.Zero && track.Track is { IsSeekable: true })
            await SeekAsync(position, cancellationToken);

        if (restore.Pause)
            await PauseAsync(cancellationToken);
    }

    private bool IsTtsItem(ITrackQueueItem? item) =>
        item?.Track is { } track && _ttsTrackIds.Contains(track.Identifier);

    /// <summary>Resets the TTS session. Callers must hold <see cref="_sync"/>.</summary>
    private void ClearTtsSession()
    {
        _ttsActive = false;
        _ttsQueue.Clear();
        _ttsTrackIds.Clear();
        _interruptedItem = null;
        _interruptedPosition = null;
        _pauseAfterTts = false;
    }

    private TimeSpan? GetPlaybackPosition()
    {
        if (CurrentTrack is not { } track || Position is not { } position)
            return null;

        // The interpolated position keeps advancing while paused, so use the last synced value instead
        TimeSpan value = IsPaused ? position.RelativePosition : position.Position;
        return value < TimeSpan.Zero ? TimeSpan.Zero : value > track.Duration ? track.Duration : value;
    }

    private static DateTimeOffset GetClipDeadline(LavalinkTrack clip)
    {
        TimeSpan duration = clip.IsLiveStream || clip.Duration <= TimeSpan.Zero || clip.Duration > UnknownClipDuration
            ? UnknownClipDuration
            : clip.Duration;
        return DateTimeOffset.UtcNow + duration + StaleSessionGrace;
    }

    private static float BoostVolume(float volume) => Math.Min(1.0f, volume * TtsVolumeBoost);

    private sealed record PendingRestore(string Identifier, TimeSpan? Position, bool Pause);

    public static ValueTask<TtsQueuedPlayer> CreateAsync(
        IPlayerProperties<TtsQueuedPlayer, QueuedLavalinkPlayerOptions> properties,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new TtsQueuedPlayer(properties));
    }
}
