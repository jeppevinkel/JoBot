namespace JoBot.Discord.Voice;

public enum VoiceBackend
{
    /// <summary>Audio is decoded and sent by a Lavalink server.</summary>
    Lavalink,

    /// <summary>Audio is decoded locally with ffmpeg and sent with DSharpPlus.Voice.</summary>
    Native
}

public class VoiceBackendOptions
{
    public const string SectionName = "Voice";

    public VoiceBackend Backend { get; init; } = VoiceBackend.Lavalink;

    /// <summary>ffmpeg executable used by the native backend.</summary>
    public string FfmpegPath { get; init; } = "ffmpeg";

    /// <summary>ffprobe executable used by the native backend to read track durations and tags.</summary>
    public string FfprobePath { get; init; } = "ffprobe";

    /// <summary>
    /// How long the bot stays in a voice channel with no other (non-bot) users before leaving.
    /// Applies to both backends. Set to 00:00:00 to never leave automatically.
    /// </summary>
    public TimeSpan AloneTimeout { get; init; } = TimeSpan.FromMinutes(10);
}
