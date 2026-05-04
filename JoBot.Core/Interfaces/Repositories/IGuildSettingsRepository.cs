using JoBot.Core.Models;

namespace JoBot.Core.Interfaces.Repositories;

public interface IGuildSettingsRepository
{
    Task<GuildSettingsUpdate?> GetOverridesAsync(ulong guildId);
    Task SaveOverridesAsync(ulong guildId, GuildSettingsUpdate overrides);
    Task ClearOverridesAsync(ulong guildId);
    Task ClearFieldAsync(ulong guildId, SettingField field);
}