namespace JoBot.Subsonic.Models;

public class SearchResult3
{
    public List<SubsonicSong>? Song { get; init; }
    public List<SubsonicAlbum>? Album { get; init; }
    public List<SubsonicArtist>? Artist { get; init; }
}