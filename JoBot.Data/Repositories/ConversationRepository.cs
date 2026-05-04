using JoBot.Core.Interfaces.Repositories;
using JoBot.Core.Models;
using JoBot.Data.Context;
using JoBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JoBot.Data.Repositories;

public class ConversationRepository : IConversationRepository
{
    private readonly IDbContextFactory<JoBotDbContext> _contextFactory;

    public ConversationRepository(IDbContextFactory<JoBotDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }
    
    public async Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(ulong guildId, int limit)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Conversations
            .Where(c => c.GuildId == guildId)
            .OrderByDescending(c => c.Timestamp)
            .Take(limit)
            .OrderBy(c => c.Timestamp)
            .Select(c => new ConversationMessage
            {
                Role = c.Role,
                ContentJson = c.Content,
                Timestamp = c.Timestamp
            })
            .ToListAsync();
    }

    public async Task AddMessageAsync(ulong guildId, ConversationMessage message)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        context.Conversations.Add(new ConversationEntity
        {
            GuildId = guildId,
            Role = message.Role,
            Content = message.ContentJson,
            Timestamp = message.Timestamp
        });

        await context.SaveChangesAsync();
    }

    public async Task ClearHistoryAsync(ulong guildId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        await context.Conversations
            .Where(c => c.GuildId == guildId)
            .ExecuteDeleteAsync();
    }
}