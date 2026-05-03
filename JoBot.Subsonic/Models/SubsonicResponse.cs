namespace JoBot.Subsonic.Models;

public class SubsonicResponse
{
    public string Status { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public SubsonicError? Error { get; init; }

    // Add a property per endpoint you use
    public SearchResult3? SearchResult3 { get; init; }
    public RandomSongs? RandomSongs { get; init; }
}