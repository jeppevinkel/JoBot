using JoBot.Core.Models;

namespace JoBot.Core.Interfaces;

public interface IVoiceService
{
    Task<bool> JoinVoiceChannelAsync(ulong guildId, ulong channelId);

    /// <summary>
    /// Leaves the guild's voice channel. Music stops straight away, but where the backend supports it, leaving
    /// waits until queued speech has finished and no <see cref="DeferLeaving"/> handles are held, so a goodbye
    /// isn't cut off.
    /// </summary>
    Task LeaveVoiceChannelAsync(ulong guildId);

    /// <summary>
    /// Keeps the bot in the guild's voice channel until the returned handle is disposed, even if asked to leave in
    /// the meantime. Hold it while answering someone, so a reply that's still being generated can be spoken.
    /// Backends that can't defer leaving return a handle that does nothing.
    /// </summary>
    IDisposable DeferLeaving(ulong guildId);
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
    /// <returns>The voice channels the bot can see, or null if the guild is unknown.</returns>
    Task<IReadOnlyList<VoiceChannelInfo>?> GetVoiceChannelsAsync(ulong guildId);

    /// <summary>
    /// Plays the track if nothing is playing, otherwise queues it. Tracks people asked for are queued in front of
    /// tracks DJ mode added, so a request doesn't wait behind the DJ's picks.
    /// </summary>
    /// <param name="isDjPick">Whether DJ mode chose this track, rather than someone asking for it.</param>
    Task<bool> EnqueueAsync(ulong guildId, string streamUrl, string? title = null, string? artist = null, string? album = null, bool isDjPick = false);
}