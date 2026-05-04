using JoBot.Core.Interfaces;
using JoBot.Core.Interfaces.Repositories;
using JoBot.Core.Models;
using Microsoft.Extensions.Options;

namespace JoBot.Services;

public class GuildSettingsService : IGuildSettingsService
{
    private readonly IGuildSettingsRepository _repository;
    private readonly GuildSettings _defaults;

    public GuildSettingsService(
        IGuildSettingsRepository repository,
        IOptions<GuildSettings> defaults)
    {
        _repository = repository;
        _defaults = defaults.Value;
    }

    public async Task<GuildSettings> GetSettingsAsync(ulong guildId)
    {
        GuildSettingsUpdate? overrides = await _repository.GetOverridesAsync(guildId);

        // Merge overrides onto defaults - null override = use default
        return new GuildSettings
        {
            SystemPrompt = overrides?.SystemPrompt ?? _defaults.SystemPrompt,
            MaxHistoryMessages = overrides?.MaxHistoryMessages ?? _defaults.MaxHistoryMessages,
            AiTemperature = overrides?.AiTemperature ?? _defaults.AiTemperature,
            MusicVolume = overrides?.MusicVolume ?? _defaults.MusicVolume
        };
    }

    public async Task UpdateSettingsAsync(ulong guildId, GuildSettingsUpdate update)
    {
        GuildSettingsUpdate existing = await _repository.GetOverridesAsync(guildId) ?? new GuildSettingsUpdate();

        // Only replace fields that are set in the update
        var merged = new GuildSettingsUpdate
        {
            SystemPrompt = update.SystemPrompt ?? existing.SystemPrompt,
            MaxHistoryMessages = update.MaxHistoryMessages ?? existing.MaxHistoryMessages,
            AiTemperature = update.AiTemperature ?? existing.AiTemperature,
            MusicVolume = update.MusicVolume ?? existing.MusicVolume
        };

        await _repository.SaveOverridesAsync(guildId, merged);
    }

    public async Task ResetSettingsAsync(ulong guildId)
    {
        await _repository.ClearOverridesAsync(guildId);
    }

    public async Task ResetFieldAsync(ulong guildId, SettingField field)
    {
        await _repository.ClearFieldAsync(guildId, field);
    }
}