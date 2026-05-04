namespace JoBot.Subsonic.Models;

public class SubsonicShare
{
    public string Id { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Username { get; init; } = string.Empty;
    public DateTime Created { get; init; }
    public DateTime? Expires { get; init; }
    public DateTime? LastVisited { get; init; }
    public int VisitCount { get; init; }
    public List<SubsonicSong>? Entry { get; init; }
}