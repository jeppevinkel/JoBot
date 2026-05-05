using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Tracks;

namespace JoBot.Discord.Lavalink.Players;

public class TtsQueuedPlayer : QueuedLavalinkPlayer
{
    private ITrackQueueItem? _interruptedItem;
    private TimeSpan? _interruptedPosition;
    private bool _playingTts;
    private string? _pendingTtsTrackId;

    public TtsQueuedPlayer(
        IPlayerProperties<TtsQueuedPlayer, QueuedLavalinkPlayerOptions> properties)
        : base(properties)
    {
    }

    public async Task PlayTtsAsync(
        LavalinkTrack ttsTrack,
        CancellationToken cancellationToken = default)
    {
        _interruptedItem = CurrentItem;
        _interruptedPosition = Position?.Position ?? null;
        _pendingTtsTrackId = ttsTrack.Identifier;

        await PlayAsync(ttsTrack, enqueue: false, cancellationToken: cancellationToken);
    }

    protected override async ValueTask NotifyTrackEndedAsync(
        ITrackQueueItem queueItem,
        TrackEndReason endReason,
        CancellationToken cancellationToken = default)
    {
        if (_playingTts && _interruptedItem is not null)
        {
            _playingTts = false;

            // Re-insert the interrupted track at the front
            // so the base queue logic picks it up next
            await Queue.InsertAsync(0, _interruptedItem, cancellationToken);
            _interruptedItem = null;
            await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken);
        }
        else
        {
            await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken);
        }
    }

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = new CancellationToken())
    {
        await base.NotifyTrackStartedAsync(track, cancellationToken);
        if (track.Track?.Identifier == _pendingTtsTrackId)
        {
            _playingTts = true;
            _pendingTtsTrackId = null;
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