namespace JoBot.Core.Models;

public record GuildSettingsUpdate
{
    public string? SystemPrompt { get; init; }
    public int? MaxHistoryMessages { get; init; }
    public decimal? AiTemperature { get; init; }
    public float? MusicVolume { get; init; }
}