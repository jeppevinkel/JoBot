// using DSharpPlus.Voice;
//
// namespace JoBot.Discord.Services;
//
// public class GuildVoiceState
// {
//     public SemaphoreSlim Lock { get; } = new(1, 1);
//     public VoiceConnection? Connection { get; set; }
//     public ulong? ChannelId { get; set; }
//     public CancellationTokenSource? PlaybackCts { get; set; }
//     public bool IsPlaying => PlaybackCts is not null;
// }