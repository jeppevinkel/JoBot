using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Tracks;

namespace JoBot.Discord.Lavalink.Players;

public class TtsQueuedPlayer : QueuedLavalinkPlayer
{
    private const float TtsVolumeBoost = 2.0f;

    private readonly Queue<LavalinkTrack> _ttsQueue = new();
    private readonly HashSet<string> _ttsTrackIds = new();
    private ITrackQueueItem? _interruptedItem;
    private TimeSpan? _interruptedPosition;
    private bool _playingTts;
    private float _preInterruptVolume;

    public TtsQueuedPlayer(
        IPlayerProperties<TtsQueuedPlayer, QueuedLavalinkPlayerOptions> properties)
        : base(properties)
    {
    }

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
        _interruptedPosition = Position?.Position;

        await PlayAsync(ttsTrack, enqueue: false, cancellationToken: cancellationToken);
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
            await SeekAsync(_interruptedPosition.Value, cancellationToken);
            _interruptedPosition = null;
        }
    }

    public static ValueTask<TtsQueuedPlayer> CreateAsync(
        IPlayerProperties<TtsQueuedPlayer, QueuedLavalinkPlayerOptions> properties,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new TtsQueuedPlayer(properties));
    }
}