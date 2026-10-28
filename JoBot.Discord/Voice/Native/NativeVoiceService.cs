using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Voice;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Voice.Listening;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.Discord.Voice.Native;

/// <summary>
/// <see cref="IVoiceService"/> that connects with DSharpPlus.Voice (including DAVE end-to-end encryption)
/// and decodes audio locally with ffmpeg, without a Lavalink server.
/// </summary>
public class NativeVoiceService : IVoiceService
{
    private readonly ILogger<NativeVoiceService> _logger;
    private readonly ILogger<NativeGuildPlayer> _playerLogger;
    private readonly DiscordClient _discordClient;
    private readonly IGuildSettingsService _settingsService;
    private readonly VoiceBackendOptions _options;
    private readonly VoiceListeningOptions _listeningOptions;
    private readonly ConcurrentDictionary<ulong, NativeGuildPlayer> _players = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _connectionLocks = new();
    private readonly ConcurrentDictionary<ulong, int> _leaveHolds = new();
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _pendingLeaves = new();

    // Leave anyway after this long, in case speech or an answer gets stuck
    private static readonly TimeSpan MaxLeaveDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeaveCheckInterval = TimeSpan.FromMilliseconds(250);

    public NativeVoiceService(
        ILogger<NativeVoiceService> logger,
        ILogger<NativeGuildPlayer> playerLogger,
        DiscordClient discordClient,
        IGuildSettingsService settingsService,
        IOptions<VoiceBackendOptions> options,
        IOptions<VoiceListeningOptions> listeningOptions)
    {
        _logger = logger;
        _playerLogger = playerLogger;
        _discordClient = discordClient;
        _settingsService = settingsService;
        _options = options.Value;
        _listeningOptions = listeningOptions.Value;
    }

    public async Task<bool> JoinVoiceChannelAsync(ulong guildId, ulong channelId)
    {
        SemaphoreSlim connectionLock = _connectionLocks.GetOrAdd(guildId, static _ => new SemaphoreSlim(1, 1));
        await connectionLock.WaitAsync();
        try
        {
            // Asked to join again before a pending leave went through: stay
            if (_pendingLeaves.TryRemove(guildId, out CancellationTokenSource? pendingLeave))
                await pendingLeave.CancelAsync();

            if (_players.TryGetValue(guildId, out NativeGuildPlayer? existing))
            {
                if (existing.IsAlive && existing.ChannelId == channelId)
                    return true;

                // Moving channels, or replacing a player whose loop died. DSharpPlus.Voice allows one
                // connection per guild, so close the old one first.
                _players.TryRemove(guildId, out _);
                await existing.DisposeAsync();
            }

            if (!_discordClient.Guilds.TryGetValue(guildId, out DiscordGuild? guild) ||
                !guild.Channels.TryGetValue(channelId, out DiscordChannel? channel))
            {
                _logger.LogWarning("Voice channel {ChannelId} not found in guild {GuildId}", channelId, guildId);
                return false;
            }

            if (channel.Type is not (DiscordChannelType.Voice or DiscordChannelType.Stage))
            {
                _logger.LogWarning("Channel {ChannelId} in guild {GuildId} is not a voice channel", channelId, guildId);
                return false;
            }

            GuildSettings settings = await _settingsService.GetSettingsAsync(guildId);

            VoiceConnection connection = await channel.ConnectAsync(
                receiverType: _listeningOptions.Enabled ? typeof(ListeningAudioReceiver) : null,
                options: new VoiceConnectionOptions
                {
                    AudioType = AudioType.Music
                });

            if (connection.Receiver is ListeningAudioReceiver receiver)
                receiver.Attach(connection, guildId);

            var player = new NativeGuildPlayer(connection, settings.MusicVolume, _options.FfmpegPath, _playerLogger);
            connection.SetDisconnectHandler(HandleDisconnectAsync, (guildId, player));
            _players[guildId] = player;

            _logger.LogInformation("Joined voice channel {ChannelId} in guild {GuildId}", channelId, guildId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining voice channel {ChannelId} in guild {GuildId}", channelId, guildId);
            return false;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    public Task LeaveVoiceChannelAsync(ulong guildId)
    {
        if (!_players.TryGetValue(guildId, out NativeGuildPlayer? player))
            return Task.CompletedTask;

        // Leaving means the music should stop now; only speech gets to finish
        player.StopMusic();

        var pendingLeave = new CancellationTokenSource();
        if (_pendingLeaves.TryAdd(guildId, pendingLeave))
            _ = LeaveWhenQuietAsync(guildId, player, pendingLeave);

        return Task.CompletedTask;
    }

    public IDisposable DeferLeaving(ulong guildId)
    {
        _leaveHolds.AddOrUpdate(guildId, 1, static (_, holds) => holds + 1);
        return new LeaveHold(this, guildId);
    }

    private async Task LeaveWhenQuietAsync(ulong guildId, NativeGuildPlayer player, CancellationTokenSource pendingLeave)
    {
        try
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + MaxLeaveDelay;
            while (DateTimeOffset.UtcNow < deadline &&
                   (_leaveHolds.GetValueOrDefault(guildId) > 0 || player.HasPendingSpeech))
                await Task.Delay(LeaveCheckInterval, pendingLeave.Token);

            SemaphoreSlim connectionLock = _connectionLocks.GetOrAdd(guildId, static _ => new SemaphoreSlim(1, 1));
            await connectionLock.WaitAsync(pendingLeave.Token);
            try
            {
                // Only leave with this exact player; it may have been replaced or disconnected meanwhile
                if (!pendingLeave.IsCancellationRequested && _players.TryRemove(KeyValuePair.Create(guildId, player)))
                {
                    await player.DisposeAsync();
                    _logger.LogInformation("Left voice channel in guild {GuildId}", guildId);
                }
            }
            finally
            {
                connectionLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Asked to join again
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving voice channel in guild {GuildId}", guildId);
        }
        finally
        {
            // Not disposed: a join may cancel it concurrently, and it holds no timers
            _pendingLeaves.TryRemove(KeyValuePair.Create(guildId, pendingLeave));
        }
    }

    private sealed class LeaveHold(NativeVoiceService service, ulong guildId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                service._leaveHolds.AddOrUpdate(guildId, 0, static (_, holds) => Math.Max(0, holds - 1));
        }
    }

    private Task HandleDisconnectAsync(VoiceDisconnectReason reason, object? state)
    {
        var (guildId, player) = ((ulong, NativeGuildPlayer))state!;

        // Explicit disconnects are cleaned up by whoever initiated them
        if (reason is VoiceDisconnectReason.Disconnected)
            return Task.CompletedTask;

        _logger.LogWarning("Voice connection in guild {GuildId} was severed: {Reason}", guildId, reason);

        // Only remove this exact player, in case a newer connection has already replaced it.
        // The library disposes the connection after this handler returns, so don't block it.
        if (_players.TryRemove(KeyValuePair.Create(guildId, player)))
            _ = Task.Run(async () => await player.DisposeAsync());

        return Task.CompletedTask;
    }

    public async Task<bool> PlayAsync(ulong guildId, string streamUrl)
    {
        if (!TryGetPlayer(guildId, out NativeGuildPlayer? player))
            return false;

        player.Play(await CreateTrackAsync(streamUrl, null, null, null));
        return true;
    }

    public Task<bool> PlayTtsAsync(ulong guildId, string ttsUrl)
    {
        if (!TryGetPlayer(guildId, out NativeGuildPlayer? player))
            return Task.FromResult(false);

        player.PlayTts(ttsUrl);
        return Task.FromResult(true);
    }

    public async Task<bool> EnqueueAsync(ulong guildId, string streamUrl, string? title = null, string? artist = null, string? album = null, bool isDjPick = false)
    {
        if (!TryGetPlayer(guildId, out NativeGuildPlayer? player))
            return false;

        player.Enqueue(await CreateTrackAsync(streamUrl, title, artist, album) with { IsDjPick = isDjPick });
        return true;
    }

    public Task StopAsync(ulong guildId)
    {
        if (TryGetPlayer(guildId, out NativeGuildPlayer? player))
            player.Stop();

        return Task.CompletedTask;
    }

    public Task SkipAsync(ulong guildId)
    {
        if (TryGetPlayer(guildId, out NativeGuildPlayer? player))
            player.Skip();

        return Task.CompletedTask;
    }

    public Task<bool> IsConnectedAsync(ulong guildId) =>
        Task.FromResult(TryGetPlayer(guildId, out _));

    public Task<bool> IsPlayingAsync(ulong guildId) =>
        Task.FromResult(TryGetPlayer(guildId, out NativeGuildPlayer? player) && player.IsPlaying);

    public Task<QueueInfo?> GetQueueAsync(ulong guildId) =>
        Task.FromResult(TryGetPlayer(guildId, out NativeGuildPlayer? player) ? player.GetQueue() : null);

    public Task<NowPlayingInfo?> GetNowPlayingAsync(ulong guildId) =>
        Task.FromResult(TryGetPlayer(guildId, out NativeGuildPlayer? player) ? player.GetNowPlaying() : null);

    public Task<PlaybackResult> PauseAsync(ulong guildId) =>
        Task.FromResult(TryGetPlayer(guildId, out NativeGuildPlayer? player) ? player.Pause() : PlaybackResult.NotConnected);

    public Task<PlaybackResult> ResumeAsync(ulong guildId) =>
        Task.FromResult(TryGetPlayer(guildId, out NativeGuildPlayer? player) ? player.Resume() : PlaybackResult.NotConnected);

    public Task<PlaybackResult> SeekAsync(ulong guildId, TimeSpan position) =>
        Task.FromResult(TryGetPlayer(guildId, out NativeGuildPlayer? player) ? player.Seek(position) : PlaybackResult.NotConnected);

    public Task<float?> GetVolumeAsync(ulong guildId) =>
        Task.FromResult(TryGetPlayer(guildId, out NativeGuildPlayer? player) ? player.Volume : (float?)null);

    public Task<PlaybackResult> SetVolumeAsync(ulong guildId, float volume)
    {
        if (float.IsNaN(volume) || volume is < 0f or > 1f)
            return Task.FromResult(PlaybackResult.InvalidArgument);

        if (!TryGetPlayer(guildId, out NativeGuildPlayer? player))
            return Task.FromResult(PlaybackResult.NotConnected);

        player.SetVolume(volume);
        return Task.FromResult(PlaybackResult.Success);
    }

    public Task<(PlaybackResult Result, TrackInfo? Track)> RemoveFromQueueAsync(ulong guildId, int index)
    {
        if (!TryGetPlayer(guildId, out NativeGuildPlayer? player))
            return Task.FromResult<(PlaybackResult, TrackInfo?)>((PlaybackResult.NotConnected, null));

        TrackInfo? track = player.RemoveFromQueue(index);
        return Task.FromResult<(PlaybackResult, TrackInfo?)>(track is null ? (PlaybackResult.InvalidArgument, null) : (PlaybackResult.Success, track));
    }

    public Task<(PlaybackResult Result, TrackInfo? Track)> MoveQueueTrackAsync(ulong guildId, int fromIndex, int toIndex)
    {
        if (!TryGetPlayer(guildId, out NativeGuildPlayer? player))
            return Task.FromResult<(PlaybackResult, TrackInfo?)>((PlaybackResult.NotConnected, null));

        TrackInfo? track = player.MoveQueueTrack(fromIndex, toIndex);
        return Task.FromResult<(PlaybackResult, TrackInfo?)>(track is null ? (PlaybackResult.InvalidArgument, null) : (PlaybackResult.Success, track));
    }

    public Task<int?> ClearQueueAsync(ulong guildId) =>
        Task.FromResult(TryGetPlayer(guildId, out NativeGuildPlayer? player) ? player.ClearQueue() : (int?)null);

    public Task<IReadOnlyList<VoiceChannelInfo>?> GetVoiceChannelsAsync(ulong guildId)
    {
        ulong? botChannelId = TryGetPlayer(guildId, out NativeGuildPlayer? player) ? player.ChannelId : null;
        return Task.FromResult(GuildVoiceChannels.List(_discordClient, guildId, botChannelId));
    }

    private bool TryGetPlayer(ulong guildId, [NotNullWhen(true)] out NativeGuildPlayer? player) =>
        _players.TryGetValue(guildId, out player) && player.IsAlive;

    private async Task<NativeTrack> CreateTrackAsync(string url, string? title, string? artist, string? album)
    {
        MediaInfo info = await MediaProbe.ProbeAsync(_options.FfprobePath, url, _logger);

        return new NativeTrack(
            url,
            Coalesce(title, info.Title) ?? "Unknown",
            Coalesce(artist, info.Artist) ?? "Unknown",
            Coalesce(album, info.Album),
            info.Duration);
    }

    private static string? Coalesce(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}
