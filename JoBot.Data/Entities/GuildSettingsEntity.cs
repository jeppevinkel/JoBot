namespace JoBot.Data.Entities;

public class GuildSettingsEntity
{
    public ulong GuildId { get; set; }

    // Nullable = not overridden, use global default
    public string? SystemPrompt { get; set; }
    public int? MaxHistoryMessages { get; set; }
    public decimal? AiTemperature { get; set; }
    public float? MusicVolume { get; set; }
}