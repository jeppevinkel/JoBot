namespace JoBot.Subsonic.Models;

public class SubsonicAlbum
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Artist { get; init; }
    public string? ArtistId { get; init; }
    public string? CoverArt { get; init; }
    public int? SongCount { get; init; }
    public int? Duration { get; init; }
    public int? Year { get; init; }
    public string? Genre { get; init; }
}