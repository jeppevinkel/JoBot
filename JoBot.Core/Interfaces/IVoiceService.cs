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
    /// <param name="index">0-based queue index.</param>
    Task<(PlaybackResult Result, TrackInfo? Track)> RemoveFromQueueAsync(ulong guildId, int index);
    /// <param name="fromIndex">0-based queue index of the track to move.</param>
    /// <param name="toIndex">0-based queue index to move the track to; clamped to the end of the queue.</param>
    Task<(PlaybackResult Result, TrackInfo? Track)> MoveQueueTrackAsync(ulong guildId, int fromIndex, int toIndex);
    /// <returns>The number of tracks removed, or null if not connected.</returns>
    Task<int?> ClearQueueAsync(ulong guildId);
    Task<bool> EnqueueAsync(ulong guildId, string streamUrl, string? title = null, string? artist = null, string? album = null);
}