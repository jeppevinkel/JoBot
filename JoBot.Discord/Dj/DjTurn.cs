namespace JoBot.Discord.Dj;

/// <summary>
/// Marks the AI turn that's topping up the queue for DJ mode, so tracks queued during it count as DJ picks.
/// </summary>
/// <remarks>
/// Stored in an <see cref="AsyncLocal{T}"/>, so it flows from <see cref="DjModeService"/> through the AI service
/// into the tools it calls, without reaching turns that answer people.
/// </remarks>
public sealed class DjTurn : IDisposable
{
    private static readonly AsyncLocal<DjTurn?> CurrentTurn = new();

    private readonly CancellationToken _sessionEnded;
    private int _tracksAdded;

    private DjTurn(CancellationToken sessionEnded)
    {
        _sessionEnded = sessionEnded;
    }

    /// <summary>The DJ turn the caller is part of, or null when answering people.</summary>
    public static DjTurn? Current => CurrentTurn.Value;

    public int TracksAdded => Volatile.Read(ref _tracksAdded);

    /// <summary>
    /// Whether DJ mode was turned off during the turn. It runs alongside people's messages, so someone may have
    /// stopped the music meanwhile; tracks added after that would start it again.
    /// </summary>
    public bool IsSessionEnded => _sessionEnded.IsCancellationRequested;

    /// <summary>Starts a DJ turn for the calling async flow. Dispose it when the turn ends.</summary>
    /// <param name="sessionEnded">Cancelled when DJ mode is turned off.</param>
    public static DjTurn Begin(CancellationToken sessionEnded) => CurrentTurn.Value = new DjTurn(sessionEnded);

    public void RecordTrackAdded() => Interlocked.Increment(ref _tracksAdded);

    public void Dispose()
    {
        if (CurrentTurn.Value == this)
            CurrentTurn.Value = null;
    }
}
