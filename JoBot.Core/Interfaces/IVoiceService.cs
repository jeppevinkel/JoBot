using JoBot.Core.Models;

namespace JoBot.Core.Interfaces;

public interface IVoiceService
{
    Task<bool> JoinVoiceChannelAsync(ulong guildId, ulong channelId);
    Task LeaveVoiceChannelAsync(ulong guildId);
    Task<bool> PlayAsync(ulong guildId, string streamUrl);
    Task<bool> PlayTtsAsync(ulong guildId, string ttsUrl);
    Task StopAsync(ulong guildId);
    Task<bool> IsConnectedAsync(ulong guildId);
    Task<bool> IsPlayingAsync(ulong guildId);
    Task<QueueInfo?> GetQueueAsync(ulong guildId);
    Task SkipAsync(ulong guildId);
    Task<NowPlayingInfo?> GetNowPlayingAsync(ulong guildId);
    Task<PlaybackResult> PauseAsync(ulong guildId);
    Task<PlaybackResult> ResumeAsync(ulong guildId);
    Task<PlaybackResult> SeekAsync(ulong guildId, TimeSpan position);
    Task<float?> GetVolumeAsync(ulong guildId);
    Task<PlaybackResult> SetVolumeAsync(ulong guildId, float volume);
    Task<bool> EnqueueAsync(ulong guildId, string streamUrl, string? title = null, string? artist = null, string? album = null);
}