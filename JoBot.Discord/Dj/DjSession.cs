using JoBot.Core.Models;

namespace JoBot.Discord.Dj;

public enum DjTopUpOutcome
{
    /// <summary>The AI added at least one track.</summary>
    ToppedUp,

    /// <summary>The AI added nothing, but will be asked again.</summary>
    Failed,

    /// <summary>The AI added nothing too many times in a row, so DJ mode is now paused.</summary>
    Paused
}

/// <param name="Paused">Whether DJ mode stopped asking for tracks after failed top-ups.</param>
public sealed record DjStatus(ulong ChannelId, string? Guidance, bool Announce, bool Paused);

/// <summary>What the AI is told when asked for more tracks.</summary>
public sealed record DjNudgeContext(
    ulong ChannelId,
    string? Guidance,
    bool Announce,
    IReadOnlyList<TrackInfo> RecentlyPlayed,
    IReadOnlyList<TrackInfo> Skipped);

/// <summary>DJ mode state for one guild. Thread-safe.</summary>
public sealed class DjSession
{
    private static readonly TimeSpan NudgeCapWindow = TimeSpan.FromHours(1);

    private readonly object _sync = new();
    private readonly int _historySize;
    private readonly LinkedList<TrackInfo> _recentlyPlayed = new();
    private readonly LinkedList<TrackInfo> _skipped = new();
    private readonly Queue<DateTimeOffset> _recentNudges = new();

    private ulong _channelId;
    private string? _guidance;
    private bool _announce;
    private bool _paused;
    private bool _nudgePending;
    private int _failedTopUps;
    private DateTimeOffset? _lastNudgeAt;
    private TrackInfo? _lastSeenTrack;

    public DjSession(int historySize)
    {
        _historySize = Math.Max(0, historySize);
    }

    public DjStatus Status
    {
        get
        {
            lock (_sync)
                return new DjStatus(_channelId, _guidance, _announce, _paused);
        }
    }

    /// <summary>Applies new settings and resumes DJ mode if it was paused.</summary>
    /// <param name="guidance">New style notes, or null to keep the current ones.</param>
    /// <param name="announce">Whether to announce top-ups, or null to keep the current setting.</param>
    public DjStatus Update(ulong channelId, string? guidance, bool? announce)
    {
        lock (_sync)
        {
            _channelId = channelId;
            if (!string.IsNullOrWhiteSpace(guidance))
                _guidance = guidance.Trim();
            if (announce is not null)
                _announce = announce.Value;
            _paused = false;
            _failedTopUps = 0;
            return new DjStatus(_channelId, _guidance, _announce, _paused);
        }
    }

    /// <summary>Remembers the track that's playing, so the AI can avoid repeating it.</summary>
    public void RecordPlaying(TrackInfo? track)
    {
        lock (_sync)
        {
            if (track is null || track == _lastSeenTrack)
                return;

            _lastSeenTrack = track;
            AddBounded(_recentlyPlayed, track);
        }
    }

    public void RecordSkip(TrackInfo track)
    {
        lock (_sync)
            AddBounded(_skipped, track);
    }

    /// <summary>
    /// Claims the right to ask the AI for more tracks, if the queue is low and DJ mode isn't paused, already asking,
    /// cooling down, or over its hourly limit. Call <see cref="CompleteNudge"/> or <see cref="CancelNudge"/> afterwards.
    /// </summary>
    public bool TryBeginNudge(int remainingTracks, DateTimeOffset now, DjModeOptions options)
    {
        lock (_sync)
        {
            if (_paused || _nudgePending || remainingTracks > options.LowQueueThreshold)
                return false;

            if (_lastNudgeAt is { } last && now - last < options.NudgeCooldown)
                return false;

            while (_recentNudges.TryPeek(out DateTimeOffset sentAt) && now - sentAt >= NudgeCapWindow)
                _recentNudges.Dequeue();

            if (_recentNudges.Count >= options.MaxNudgesPerHour)
                return false;

            _nudgePending = true;
            return true;
        }
    }

    /// <summary>Records that the AI is being asked for tracks now, and returns what to tell it.</summary>
    public DjNudgeContext MarkNudgeSent(DateTimeOffset now)
    {
        lock (_sync)
        {
            _lastNudgeAt = now;
            _recentNudges.Enqueue(now);
            return new DjNudgeContext(_channelId, _guidance, _announce, _recentlyPlayed.ToList(), _skipped.ToList());
        }
    }

    /// <summary>Gives up on a nudge that turned out not to be needed.</summary>
    public void CancelNudge()
    {
        lock (_sync)
            _nudgePending = false;
    }

    public DjTopUpOutcome CompleteNudge(int tracksAdded, int maxRetries)
    {
        lock (_sync)
        {
            _nudgePending = false;

            if (tracksAdded > 0)
            {
                _failedTopUps = 0;
                return DjTopUpOutcome.ToppedUp;
            }

            _failedTopUps++;
            if (_failedTopUps < maxRetries)
                return DjTopUpOutcome.Failed;

            _paused = true;
            return DjTopUpOutcome.Paused;
        }
    }

    public int FailedTopUps
    {
        get
        {
            lock (_sync)
                return _failedTopUps;
        }
    }

    private void AddBounded(LinkedList<TrackInfo> list, TrackInfo track)
    {
        list.AddLast(track);
        while (list.Count > _historySize)
            list.RemoveFirst();
    }
}
