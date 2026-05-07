using DSharpPlus.Voice;

namespace JoBot.Discord.Services;

public class GuildVoiceState
{
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public VoiceConnection? Connection { get; set; }
    public ulong? ChannelId { get; set; }
    public CancellationTokenSource? PlaybackCts { get; set; }
    public CancellationTokenSource? TrackSkipCts { get; set; }
    public bool IsPlaying => PlaybackCts is not null;
    public Queue<string> TrackQueue { get; } = new();
    public string? CurrentTrackUrl { get; set; }

    // Tracks the running StreamAudioAsync task so reconnect logic can await its cleanup.
    public Task? PlaybackTask { get; set; }

    // TTS mixing: raw S16LE stereo 48kHz PCM loaded into memory, mixed over ongoing music.
    public byte[]? TtsPcmData { get; set; }
    public int TtsPcmOffset { get; set; }
}
