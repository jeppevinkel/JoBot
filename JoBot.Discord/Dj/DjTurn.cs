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

    private int _tracksAdded;

    private DjTurn()
    {
    }

    /// <summary>The DJ turn the caller is part of, or null when answering people.</summary>
    public static DjTurn? Current => CurrentTurn.Value;

    public int TracksAdded => Volatile.Read(ref _tracksAdded);

    /// <summary>Starts a DJ turn for the calling async flow. Dispose it when the turn ends.</summary>
    public static DjTurn Begin() => CurrentTurn.Value = new DjTurn();

    public void RecordTrackAdded() => Interlocked.Increment(ref _tracksAdded);

    public void Dispose()
    {
        if (CurrentTurn.Value == this)
            CurrentTurn.Value = null;
    }
}
