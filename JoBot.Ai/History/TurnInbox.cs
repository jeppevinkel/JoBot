namespace JoBot.Ai.History;

/// <summary>
/// Messages for the bot that arrive in a channel while the AI is already answering someone there. Instead of
/// waiting for that answer to finish, they're added to it at its next tool call.
/// </summary>
public sealed class TurnInbox
{
    private readonly object _sync = new();
    private readonly List<(string Message, TaskCompletionSource<bool> Accepted)> _pending = [];
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closed;

    public TurnInbox(string channelKey)
    {
        ChannelKey = channelKey;
    }

    /// <summary>Which channel the turn answers in; only messages from there can join it.</summary>
    public string ChannelKey { get; }

    /// <summary>Completes once the turn has finished.</summary>
    public Task Completed => _completed.Task;

    /// <summary>Offers a serialized message to the turn.</summary>
    /// <returns>
    /// A task that completes with true once the message is part of the turn, or false if the turn finished without
    /// taking it; null if the turn is already finishing.
    /// </returns>
    public Task<bool>? TryPost(string message)
    {
        lock (_sync)
        {
            if (_closed)
                return null;

            var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add((message, accepted));
            return accepted.Task;
        }
    }

    /// <summary>Takes the messages that arrived since the last call.</summary>
    public IReadOnlyList<string> Drain()
    {
        lock (_sync)
        {
            var messages = _pending.Select(p => p.Message).ToList();
            foreach (var (_, accepted) in _pending)
                accepted.TrySetResult(true);
            _pending.Clear();
            return messages;
        }
    }

    /// <summary>Ends the turn. Messages it didn't get to are turned away, to be answered on their own.</summary>
    public void Close()
    {
        lock (_sync)
        {
            _closed = true;
            foreach (var (_, accepted) in _pending)
                accepted.TrySetResult(false);
            _pending.Clear();
        }

        _completed.TrySetResult();
    }
}
