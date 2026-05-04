using JoBot.Core.Models;

namespace JoBot.Core.Interfaces;

public interface IGuildSettingsService
{
    Task<GuildSettings> GetSettingsAsync(ulong guildId);
    Task UpdateSettingsAsync(ulong guildId, GuildSettingsUpdate update);
    Task ResetSettingsAsync(ulong guildId);
    Task ResetFieldAsync(ulong guildId, SettingField field);
}