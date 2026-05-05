using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Lavalink.Players;
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
    private readonly ILogger<LavalinkVoiceService> _logger;
    private readonly IAudioService _audioService;

    public LavalinkVoiceService(
        ILogger<LavalinkVoiceService> logger,
        IAudioService audioService)
    {
        _logger = logger;
        _audioService = audioService;
    }

    public async Task<bool> JoinVoiceChannelAsync(ulong guildId, ulong channelId)
    {
        try
        {
            var options = new QueuedLavalinkPlayerOptions
            {
                InitialVolume = 0.5f
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

    public async Task<bool> EnqueueAsync(ulong guildId, string streamUrl)
    {
        try
        {
            var player = await GetPlayerAsync(guildId);
            if (player is null) return false;

            var track = await _audioService.Tracks.LoadTrackAsync(streamUrl, TrackSearchMode.None);
            if (track is null) return false;

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
        await player.SkipAsync();
    }

    public async Task<QueueInfo?> GetQueueAsync(ulong guildId)
    {
        TtsQueuedPlayer? player = await GetPlayerAsync(guildId);
        if (player is null)
            return null;

        LavalinkTrack? current = player.CurrentTrack;
        ITrackQueue queue = player.Queue;

        return new QueueInfo
        {
            CurrentTrack = current is null
                ? null
                : new TrackInfo
                {
                    Title = current.Title,
                    Artist = current.Author,
                    Duration = current.Duration
                },
            QueuedTracks = queue.Select(item => new TrackInfo
            {
                Title = item.Track?.Title ?? "Unknown",
                Artist = item.Track?.Author ?? "Unknown",
                Duration = item.Track?.Duration ?? TimeSpan.Zero
            }).ToList()
        };
    }

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