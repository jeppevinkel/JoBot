using System.Reflection;
using JoBot.Core.Interfaces.Repositories;
using JoBot.Core.Models;
using JoBot.Data.Context;
using JoBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JoBot.Data.Repositories;

public class GuildSettingsRepository : IGuildSettingsRepository
{
    private readonly IDbContextFactory<JoBotDbContext> _contextFactory;

    public GuildSettingsRepository(IDbContextFactory<JoBotDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }
    
    public async Task<GuildSettingsUpdate?> GetOverridesAsync(ulong guildId)
    {
        await using JoBotDbContext context = await _contextFactory.CreateDbContextAsync();

        GuildSettingsEntity? entity = await context.GuildSettings
            .FirstOrDefaultAsync(c => c.GuildId == guildId);
        
        if (entity is null) return null;

        return new GuildSettingsUpdate
        {
            SystemPrompt = entity.SystemPrompt,
            MaxHistoryMessages = entity.MaxHistoryMessages,
            AiTemperature = entity.AiTemperature,
            MusicVolume = entity.MusicVolume
        };
    }

    public async Task SaveOverridesAsync(ulong guildId, GuildSettingsUpdate overrides)
    {
        await using JoBotDbContext context = await _contextFactory.CreateDbContextAsync();
        
        GuildSettingsEntity? existing = await context.GuildSettings.FirstOrDefaultAsync(c => c.GuildId == guildId);

        if (existing is not null)
        {
            existing.SystemPrompt = overrides.SystemPrompt ?? existing.SystemPrompt;
            existing.MaxHistoryMessages = overrides.MaxHistoryMessages ?? existing.MaxHistoryMessages;
            existing.AiTemperature = overrides.AiTemperature ?? existing.AiTemperature;
            existing.MusicVolume = overrides.MusicVolume ?? existing.MusicVolume;
        } else
        {
            var entity = new GuildSettingsEntity
            {
                GuildId = guildId,
                SystemPrompt = overrides.SystemPrompt,
                MaxHistoryMessages = overrides.MaxHistoryMessages,
                AiTemperature = overrides.AiTemperature,
                MusicVolume = overrides.MusicVolume
            };
            context.GuildSettings.Add(entity);
        }
        await context.SaveChangesAsync();
    }

    public async Task ClearOverridesAsync(ulong guildId)
    {
        await using JoBotDbContext context = await _contextFactory.CreateDbContextAsync();
        
        GuildSettingsEntity? existing = await context.GuildSettings.FirstOrDefaultAsync(c => c.GuildId == guildId);

        if (existing is not null)
        {
            context.GuildSettings.Remove(existing);
            await context.SaveChangesAsync();
        }
    }

    public async Task ClearFieldAsync(ulong guildId, SettingField field)
    {
        await using JoBotDbContext context = await _contextFactory.CreateDbContextAsync();
        
        GuildSettingsEntity? existing = await context.GuildSettings
            .FirstOrDefaultAsync(c => c.GuildId == guildId);
        
        if (existing is null) return;
        
        switch (field)
        {
            case SettingField.SystemPrompt:
                existing.SystemPrompt = null;
                break;
            case SettingField.MaxHistoryMessages:
                existing.MaxHistoryMessages = null;
                break;
            case SettingField.AiTemperature:
                existing.AiTemperature = null;
                break;
            case SettingField.MusicVolume:
                existing.MusicVolume = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
        
        await context.SaveChangesAsync();
    }
}