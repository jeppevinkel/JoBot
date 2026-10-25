namespace JoBot.Discord.Voice.Native;

/// <param name="Duration">The track length, or null if unknown (e.g. a live stream).</param>
public sealed record NativeTrack(string Url, string Title, string Artist, string? Album, TimeSpan? Duration)
{
    public bool IsSeekable => Duration is not null;
}
