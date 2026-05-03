namespace JoBot.Subsonic.Models;

public class SubsonicArtist
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? CoverArt { get; init; }
    public int? AlbumCount { get; init; }
}