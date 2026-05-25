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
    Task<bool> EnqueueAsync(ulong guildId, string streamUrl, string? title = null, string? artist = null, string? album = null);
}