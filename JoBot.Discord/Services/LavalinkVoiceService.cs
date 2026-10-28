using System.Text.Json;
using DSharpPlus;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Lavalink.Players;
using JoBot.Discord.Voice;
using Lavalink4NET;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.Discord.Services;

public class LavalinkVoiceService : IVoiceService
{
    private const string AlbumMetadataKey = "jobot.album";

    private readonly ILogger<LavalinkVoiceService> _logger;
    private readonly IAudioService _audioService;
    private readonly IGuildSettingsService _settingsService;
    private readonly DiscordClient _discordClient;

    public LavalinkVoiceService(
        ILogger<LavalinkVoiceService> logger,
        IAudioService audioService,
        IGuildSettingsService settingsService,
        DiscordClient discordClient)
    {
        _logger = logger;
        _audioService = audioService;
        _settingsService = settingsService;
        _discordClient = discordClient;
    }

    public async Task<bool> JoinVoiceChannelAsync(ulong guildId, ulong channelId)
    {
        try
        {
            GuildSettings guildSettings = await _settingsService.GetSettingsAsync(guildId);

            var options = new QueuedLavalinkPlayerOptions
            {
                InitialVolume = guildSettings.MusicVolume
            };

            await _audioService.Players.JoinAsync<TtsQueuedPlayer, QueuedLavalinkPlayerOptions>(
                guildId,
                channelId,
                TtsQueuedPlayer.CreateAsync,
                Options.Create(options));

            _logger.LogInformation("Joined voice channel {ChannelId} in guild {GuildId}", channelId, guildId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining voice channel {ChannelId} in guild {GuildId}", channelId, guildId);
            return false;
        }
    }

    /// <summary>Lavalink leaves straight away, so this does nothing.</summary>
    public IDisposable DeferLeaving(ulong guildId) => NoLeaveHold.Instance;

    private sealed class NoLeaveHold : IDisposable
    {
        public static readonly NoLeaveHold Instance = new();

        public void Dispose()
        {
        }
    }

    public async Task LeaveVoiceChannelAsync(ulong guildId)
    {
        try
        {
            var player = await GetPlayerAsync(guildId);
            if (player is null) return;

            _logger.LogInformation("Attempting to leave voice channel in guild {GuildId}", guildId);
            await player.DisconnectAsync();
            _logger.LogInformation("Left voice channel in guild {GuildId}", guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving voice channel in guild {GuildId}", guildId);
        }
    }

    public async Task<bool> PlayAsync(ulong guildId, string streamUrl)
    {
        try
        {
            var player = await GetPlayerAsync(guildId);
            if (player is null)
            {
                _logger.LogWarning("No player found for guild {GuildId}", guildId);
                return false;
            }

            var track = await _audioService.Tracks.LoadTrackAsync(
                streamUrl,
                TrackSearchMode.None); // Direct URL, no search needed

            if (track is null)
            {
                _logger.LogWarning("Could not load track from URL for guild {GuildId}", guildId);
                return false;
            }

            await player.PlayAsync(track);

            _logger.LogInformation("Started playback in guild {GuildId}", guildId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting playback in guild {GuildId}", guildId);
            return false;
        }
    }

    public async Task<bool> PlayTtsAsync(ulong guildId, string ttsUrl)
    {
        try
        {
            var player = await GetPlayerAsync(guildId);
            if (player is null)
            {
                _logger.LogWarning("No player found for guild {GuildId}", guildId);
                return false;
            }

            var track = await _audioService.Tracks.LoadTrackAsync(
                ttsUrl,
                TrackSearchMode.None);

            if (track is null)
            {
                _logger.LogWarning("Could not load TTS track for guild {GuildId}", guildId);
                return false;
            }

            await player.PlayTtsAsync(track);

            _logger.LogInformation("Playing TTS in guild {GuildId}", guildId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing TTS for guild {GuildId}", guildId);
            return false;
        }
    }

    public async Task<bool> EnqueueAsync(ulong guildId, string streamUrl, string? title = null, string? artist = null, string? album = null)
    {
        try
        {
            var player = await GetPlayerAsync(guildId);
            if (player is null) return false;

            var track = await _audioService.Tracks.LoadTrackAsync(streamUrl, TrackSearchMode.None);
            if (track is null) return false;

            track = ApplyMetadata(track, title, artist, album);

            // If nothing is playing, start immediately
            // Otherwise add to queue
            if (player.State == PlayerState.NotPlaying)
                await player.PlayAsync(track);
            else
                await player.Queue.AddAsync(new TrackQueueItem(track));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enqueueing track for guild {GuildId}", guildId);
            return false;
        }
    }

    public async Task SkipAsync(ulong guildId)
    {
        TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
        if (player is null) return;
        await player.SkipMusicAsync();
    }

    public async Task<NowPlayingInfo?> GetNowPlayingAsync(ulong guildId)
    {
        TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
        if (player is null)
            return null;

        LavalinkTrack? track = player.CurrentMusicTrack;
        TimeSpan? position = player.MusicPosition;

        return new NowPlayingInfo
        {
            Track = track is null ? null : ToTrackInfo(track),
            // Whole seconds are precise enough and keep the output readable
            Position = track is null || position is null ? null : TimeSpan.FromSeconds(Math.Floor(position.Value.TotalSeconds)),
            IsPaused = track is not null && player.IsMusicPaused,
            IsPlayingTts = player.IsPlayingTts
        };
    }

    public Task<PlaybackResult> PauseAsync(ulong guildId) =>
        ExecuteOnMusicAsync(guildId, "pausing playback", async (player, _) =>
        {
            await player.PauseMusicAsync();
            return PlaybackResult.Success;
        });

    public Task<PlaybackResult> ResumeAsync(ulong guildId) =>
        ExecuteOnMusicAsync(guildId, "resuming playback", async (player, _) =>
        {
            await player.ResumeMusicAsync();
            return PlaybackResult.Success;
        });

    public Task<PlaybackResult> SeekAsync(ulong guildId, TimeSpan position) =>
        ExecuteOnMusicAsync(guildId, "seeking", async (player, track) =>
        {
            if (!track.IsSeekable || track.IsLiveStream)
                return PlaybackResult.NotSeekable;

            if (position < TimeSpan.Zero || position > track.Duration)
                return PlaybackResult.InvalidArgument;

            await player.SeekMusicAsync(position);
            return PlaybackResult.Success;
        });

    public async Task<float?> GetVolumeAsync(ulong guildId)
    {
        TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
        return player?.MusicVolume;
    }

    public async Task<PlaybackResult> SetVolumeAsync(ulong guildId, float volume)
    {
        if (float.IsNaN(volume) || volume is < 0f or > 1f)
            return PlaybackResult.InvalidArgument;

        try
        {
            TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
            if (player is null)
                return PlaybackResult.NotConnected;

            await player.SetMusicVolumeAsync(volume);
            return PlaybackResult.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting volume for guild {GuildId}", guildId);
            return PlaybackResult.Failed;
        }
    }

    public async Task<(PlaybackResult Result, TrackInfo? Track)> RemoveFromQueueAsync(ulong guildId, int index)
    {
        try
        {
            TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
            if (player is null)
                return (PlaybackResult.NotConnected, null);

            ITrackQueue queue = player.Queue;
            if (index < 0 || index >= queue.Count)
                return (PlaybackResult.InvalidArgument, null);

            // Remove by reference so a track advancing in the meantime can't shift us onto the wrong item
            ITrackQueueItem item = queue[index];
            if (!await queue.RemoveAsync(item))
                return (PlaybackResult.InvalidArgument, null);

            return (PlaybackResult.Success, ToQueueItemInfo(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing track from queue in guild {GuildId}", guildId);
            return (PlaybackResult.Failed, null);
        }
    }

    public async Task<(PlaybackResult Result, TrackInfo? Track)> MoveQueueTrackAsync(ulong guildId, int fromIndex, int toIndex)
    {
        try
        {
            TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
            if (player is null)
                return (PlaybackResult.NotConnected, null);

            ITrackQueue queue = player.Queue;
            if (fromIndex < 0 || fromIndex >= queue.Count || toIndex < 0)
                return (PlaybackResult.InvalidArgument, null);

            ITrackQueueItem item = queue[fromIndex];
            if (!await queue.RemoveAsync(item))
                return (PlaybackResult.InvalidArgument, null);

            try
            {
                await queue.InsertAsync(Math.Min(toIndex, queue.Count), item);
            }
            catch
            {
                // Don't lose the track if the insert fails
                await queue.AddAsync(item);
                throw;
            }

            return (PlaybackResult.Success, ToQueueItemInfo(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving track in queue in guild {GuildId}", guildId);
            return (PlaybackResult.Failed, null);
        }
    }

    public async Task<int?> ClearQueueAsync(ulong guildId)
    {
        TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
        if (player is null)
            return null;

        return await player.Queue.ClearAsync();
    }

    public async Task<IReadOnlyList<VoiceChannelInfo>?> GetVoiceChannelsAsync(ulong guildId)
    {
        TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
        return GuildVoiceChannels.List(_discordClient, guildId, player?.VoiceChannelId);
    }

    /// <summary>
    /// Runs <paramref name="action"/> against the current music track, handling the not-connected,
    /// nothing-playing and error cases.
    /// </summary>
    private async Task<PlaybackResult> ExecuteOnMusicAsync(
        ulong guildId,
        string description,
        Func<TtsQueuedPlayer, LavalinkTrack, Task<PlaybackResult>> action)
    {
        try
        {
            TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
            if (player is null)
                return PlaybackResult.NotConnected;

            LavalinkTrack? track = player.CurrentMusicTrack;
            if (track is null)
                return PlaybackResult.NothingPlaying;

            return await action(player, track);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error {Action} in guild {GuildId}", description, guildId);
            return PlaybackResult.Failed;
        }
    }

    public async Task<QueueInfo?> GetQueueAsync(ulong guildId)
    {
        TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
        if (player is null)
            return null;

        LavalinkTrack? current = player.CurrentMusicTrack;
        ITrackQueue queue = player.Queue;

        return new QueueInfo
        {
            CurrentTrack = current is null ? null : ToTrackInfo(current),
            QueuedTracks = queue.Select((item, index) => new QueuedTrackInfo
            {
                Position = index + 1,
                Title = item.Track?.Title ?? "Unknown",
                Artist = item.Track?.Author ?? "Unknown",
                Duration = item.Track?.Duration ?? TimeSpan.Zero,
                Album = item.Track is null ? null : GetAlbum(item.Track)
            }).ToList()
        };
    }

    private static TrackInfo ToTrackInfo(LavalinkTrack track) => new()
    {
        Title = track.Title,
        Artist = track.Author,
        Duration = track.Duration,
        Album = GetAlbum(track)
    };

    private static TrackInfo ToQueueItemInfo(ITrackQueueItem item) =>
        item.Track is null
            ? new TrackInfo { Title = "Unknown", Artist = "Unknown", Duration = TimeSpan.Zero }
            : ToTrackInfo(item.Track);

    private static LavalinkTrack ApplyMetadata(LavalinkTrack track, string? title, string? artist, string? album)
    {
        var hasTitle = !string.IsNullOrWhiteSpace(title);
        var hasArtist = !string.IsNullOrWhiteSpace(artist);
        var hasAlbum = !string.IsNullOrWhiteSpace(album);
        if (!hasTitle && !hasArtist && !hasAlbum) return track;

        var additionalInfo = track.AdditionalInformation;
        if (hasAlbum)
            additionalInfo = additionalInfo.SetItem(AlbumMetadataKey, JsonSerializer.SerializeToElement(album));

        return new LavalinkTrack
        {
            Title = hasTitle ? title! : track.Title,
            Author = hasArtist ? artist! : track.Author,
            Identifier = track.Identifier,
            Duration = track.Duration,
            IsSeekable = track.IsSeekable,
            IsLiveStream = track.IsLiveStream,
            Uri = track.Uri,
            ArtworkUri = track.ArtworkUri,
            Isrc = track.Isrc,
            SourceName = track.SourceName,
            ProbeInfo = track.ProbeInfo,
            StartPosition = track.StartPosition,
            AdditionalInformation = additionalInfo
        };
    }

    private static string? GetAlbum(LavalinkTrack track) =>
        track.AdditionalInformation.TryGetValue(AlbumMetadataKey, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public async Task StopAsync(ulong guildId)
    {
        var player = await GetPlayerAsync(guildId);
        if (player is null) return;

        await player.StopAsync();
    }

    public async Task<bool> IsConnectedAsync(ulong guildId)
    {
        var player = await GetPlayerAsync(guildId);
        return player is not null;
    }

    public async Task<bool> IsPlayingAsync(ulong guildId)
    {
        var player = await GetPlayerAsync(guildId);
        return player?.State == PlayerState.Playing;
    }

    private async Task<TtsQueuedPlayer?> GetPlayerAsync(ulong guildId)
    {
        var result = await _audioService.Players
            .GetPlayerAsync<TtsQueuedPlayer>(guildId);

        return result;
    }
}