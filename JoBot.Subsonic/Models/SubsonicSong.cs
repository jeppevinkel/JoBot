namespace JoBot.Subsonic.Models;

public class SubsonicSong
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Artist { get; init; }
    public string? ArtistId { get; init; }
    public string? Album { get; init; }
    public string? AlbumId { get; init; }
    public string? CoverArt { get; init; }
    public int Duration { get; init; }
    public int? BitRate { get; init; }
    public int? Track { get; init; }
    public int? Year { get; init; }
    public string? Genre { get; init; }
    public long? Size { get; init; }
    public string? ContentType { get; init; }
    public string? Suffix { get; init; }
    public string? Path { get; init; }
}