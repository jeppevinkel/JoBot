namespace JoBot.Discord.Voice.Native;

/// <param name="Duration">The track length, or null if unknown (e.g. a live stream).</param>
/// <param name="IsDjPick">Whether DJ mode added this track, rather than someone asking for it.</param>
public sealed record NativeTrack(string Url, string Title, string Artist, string? Album, TimeSpan? Duration, bool IsDjPick = false)
{
    public bool IsSeekable => Duration is not null;
}
