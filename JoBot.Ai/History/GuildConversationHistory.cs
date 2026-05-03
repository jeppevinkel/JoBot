using Anthropic.SDK.Messaging;
using JoBot.Ai.Configuration;

namespace JoBot.Ai.History;

public class GuildConversationHistory
{
    private readonly List<Message> _messages = [];
    private readonly int _maxMessages;

    public SemaphoreSlim Lock { get; } = new(1, 1);
    public IReadOnlyList<Message> Messages => _messages;

    public GuildConversationHistory(AiOptions options)
    {
        _maxMessages = options.MaxHistoryMessages;
    }

    public void Add(Message message)
    {
        _messages.Add(message);
        TrimIfNeeded();
    }

    public void Clear() => _messages.Clear();

    private void TrimIfNeeded()
    {
        while (_messages.Count > _maxMessages)
            _messages.RemoveRange(0, 2);
    }
}