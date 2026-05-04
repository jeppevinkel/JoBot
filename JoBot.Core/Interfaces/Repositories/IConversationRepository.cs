using JoBot.Core.Models;

namespace JoBot.Core.Interfaces.Repositories;

public interface IConversationRepository
{
    Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(ulong guildId, int limit);
    Task AddMessageAsync(ulong guildId, ConversationMessage message);
    Task ClearHistoryAsync(ulong guildId);
}