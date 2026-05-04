using System.Text.Json;
using Anthropic.SDK.Messaging;
using JoBot.Ai.Configuration;
using JoBot.Ai.History.Serialization;
using JoBot.Core.Interfaces;
using JoBot.Core.Interfaces.Repositories;
using JoBot.Core.Models;

namespace JoBot.Ai.History;

public class GuildConversationHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly ulong _guildId;
    private readonly IConversationRepository _repository;
    private readonly IGuildSettingsService _settingsService;
    private readonly List<Message> _messages = [];
    private bool _initialized;

    public SemaphoreSlim Lock { get; } = new(1, 1);
    public IReadOnlyList<Message> Messages => _messages;

    public GuildConversationHistory(
        ulong guildId,
        IConversationRepository repository,
        IGuildSettingsService settingsService)
    {
        _guildId = guildId;
        _repository = repository;
        _settingsService = settingsService;
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        GuildSettings settings = await _settingsService.GetSettingsAsync(_guildId);

        // Load extra to account for potential orphaned messages at start
        var stored = await _repository.GetHistoryAsync(_guildId, settings.MaxHistoryMessages * 2);
        _messages.AddRange(stored.Select(m => MessageSerializer.Deserialize(m.ContentJson)));

        SanitizeStart();
        TrimIfNeeded(settings.MaxHistoryMessages);

        _initialized = true;
    }

    public async Task AddAsync(Message message)
    {
        _messages.Add(message);

        GuildSettings settings = await _settingsService.GetSettingsAsync(_guildId);
        TrimIfNeeded(settings.MaxHistoryMessages);

        await _repository.AddMessageAsync(_guildId, new ConversationMessage
        {
            Role = message.Role == RoleType.User ? "user" : "assistant",
            ContentJson = MessageSerializer.Serialize(message),
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public async Task ClearAsync()
    {
        _messages.Clear();
        _initialized = false;
        await _repository.ClearHistoryAsync(_guildId);
    }

    private void TrimIfNeeded(int maxMessages)
    {
        while (_messages.Count > maxMessages)
        {
            // Find where the next clean turn starts
            // so we never cut mid tool_use/tool_result sequence
            var nextBoundary = FindNextTurnBoundary();
            if (nextBoundary <= 0) break;
            _messages.RemoveRange(0, nextBoundary);
        }

        SanitizeStart();
    }

    // Finds the index of the next regular user message (not a tool result)
    // Everything before that index is safe to remove as a complete unit
    private int FindNextTurnBoundary()
    {
        for (var i = 1; i < _messages.Count; i++)
        {
            if (_messages[i].Role == RoleType.User && !IsToolResult(_messages[i]))
                return i;
        }
        return -1;
    }

    // Removes any leading tool_result messages that have no preceding tool_use
    private void SanitizeStart()
    {
        while (_messages.Count > 0 && IsToolResult(_messages[0]))
            _messages.RemoveAt(0);
    }

    private static bool IsToolResult(Message message) =>
        message.Content.Count > 0 && message.Content.All(c => c is ToolResultContent);
}