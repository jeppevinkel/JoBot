using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Voice;
using DSharpPlus.Voice.Codec;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Extensions;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Services;

public class DiscordVoiceService : IVoiceService
{
    // S16LE stereo 48kHz = 192 000 bytes/sec. Stay at most 1 second ahead so that a skip
    // takes effect within ~1 second rather than draining a multi-second pre-fill.
    private const long MaxAheadBytes = 192_000;

    // When TTS is active the music is ducked and TTS is amplified so it's audible over it.
    private const float MusicDuckFactor = 0.4f;
    private const float TtsAmplification = 2.0f;

    private readonly ILogger<DiscordVoiceService> _logger;
    private readonly DiscordClient _discordClient;
    private readonly ConcurrentDictionary<ulong, GuildVoiceState> _guildStates = new();

    public DiscordVoiceService(
        ILogger<DiscordVoiceService> logger,
        DiscordClient discordClient)
    {
        _logger = logger;
        _discordClient = discordClient;
    }

    public async Task<bool> JoinVoiceChannelAsync(ulong guildId, ulong channelId)
    {
        GuildVoiceState state = _guildStates.GetOrAdd(guildId, static _ => new GuildVoiceState());

        await state.Lock.WaitAsync();
        try
        {
            if (state.Connection is not null)
            {
                _logger.LogInformation(
                    "Already connected to channel {ChannelId} in guild {GuildId}, disconnecting first",
                    state.ChannelId, guildId);
                await DisconnectAsync(state);
            }

            var (guild, reason) = await _discordClient.TryGetGuildWithReasonAsync(guildId, _logger);
            if (guild is null)
            {
                _logger.LogWarning("Could not find guild {GuildId}: {Reason}", guildId, reason);
                return false;
            }

            DiscordChannel channel;
            try
            {
                channel = await guild.GetChannelAsync(channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get channel {ChannelId} in guild {GuildId}", channelId, guildId);
                return false;
            }

            if (channel.Type != DiscordChannelType.Voice)
            {
                _logger.LogWarning("Channel {ChannelId} in guild {GuildId} is not a voice channel", channelId, guildId);
                return false;
            }

            state.Connection = await channel.ConnectAsync(AudioType.Voice);
            state.Connection.SetDisconnectHandler(HandleVoiceDisconnectAsync, (guildId, channelId));
            state.ChannelId = channelId;

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
            state.Lock.Release();
        }
    }

    public async Task LeaveVoiceChannelAsync(ulong guildId)
    {
        if (!_guildStates.TryGetValue(guildId, out var state))
        {
            _logger.LogDebug("No voice state found for guild {GuildId}", guildId);
            return;
        }

        await state.Lock.WaitAsync();
        try
        {
            if (state.Connection is null)
            {
                _logger.LogDebug("Not connected to voice in guild {GuildId}", guildId);
                return;
            }

            await CancelPlaybackAsync(state);
            await DisconnectAsync(state);
            _logger.LogInformation("Left voice channel in guild {GuildId}", guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving voice channel in guild {GuildId}", guildId);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task<bool> PlayAsync(ulong guildId, string streamUrl)
    {
        if (!_guildStates.TryGetValue(guildId, out var state))
        {
            _logger.LogWarning("No voice state found for guild {GuildId}", guildId);
            return false;
        }

        await state.Lock.WaitAsync();
        try
        {
            if (state.Connection is null)
            {
                _logger.LogWarning("Not connected to voice in guild {GuildId}", guildId);
                return false;
            }

            if (state.IsPlaying)
                await CancelPlaybackAsync(state);

            state.TrackQueue.Clear();
            state.CurrentTrackUrl = streamUrl;
            state.PlaybackCts = new CancellationTokenSource();
        }
        finally
        {
            state.Lock.Release();
        }

        state.PlaybackTask = Task.Run(() => StreamAudioAsync(guildId, state, streamUrl, state.PlaybackCts!.Token));
        return true;
    }

    public async Task<bool> PlayTtsAsync(ulong guildId, string ttsUrl)
    {
        if (!_guildStates.TryGetValue(guildId, out var state))
            return false;

        bool musicPlaying;
        await state.Lock.WaitAsync();
        try
        {
            if (state.Connection is null) return false;
            musicPlaying = state.IsPlaying;
        }
        finally
        {
            state.Lock.Release();
        }

        // No music playing — treat TTS as a normal track.
        if (!musicPlaying)
            return await PlayAsync(guildId, ttsUrl);

        // Music is active — load TTS PCM into memory and mix it over the music stream.
        byte[]? ttsPcm = await LoadPcmAsync(ttsUrl, CancellationToken.None);
        if (ttsPcm is null || ttsPcm.Length == 0) return false;

        await state.Lock.WaitAsync();
        try
        {
            state.TtsPcmData = ttsPcm;
            state.TtsPcmOffset = 0;
        }
        finally
        {
            state.Lock.Release();
        }

        return true;
    }

    public async Task<bool> EnqueueAsync(ulong guildId, string streamUrl)
    {
        if (!_guildStates.TryGetValue(guildId, out var state))
        {
            _logger.LogWarning("No voice state found for guild {GuildId}", guildId);
            return false;
        }

        await state.Lock.WaitAsync();
        try
        {
            if (state.Connection is null)
            {
                _logger.LogWarning("Not connected to voice in guild {GuildId}", guildId);
                return false;
            }

            if (!state.IsPlaying)
            {
                state.CurrentTrackUrl = streamUrl;
                state.PlaybackCts = new CancellationTokenSource();
                state.PlaybackTask = Task.Run(() => StreamAudioAsync(guildId, state, streamUrl, state.PlaybackCts!.Token));
            }
            else
            {
                state.TrackQueue.Enqueue(streamUrl);
            }

            return true;
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task SkipAsync(ulong guildId)
    {
        if (!_guildStates.TryGetValue(guildId, out var state)) return;

        await state.Lock.WaitAsync();
        try
        {
            if (state.TrackSkipCts is not null)
                await state.TrackSkipCts.CancelAsync();
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public async Task StopAsync(ulong guildId)
    {
        if (!_guildStates.TryGetValue(guildId, out var state)) return;

        await state.Lock.WaitAsync();
        try
        {
            state.TrackQueue.Clear();
            await CancelPlaybackAsync(state);
        }
        finally
        {
            state.Lock.Release();
        }
    }

    public Task<bool> IsPlayingAsync(ulong guildId) =>
        Task.FromResult(_guildStates.TryGetValue(guildId, out var state) && state.IsPlaying);

    public Task<bool> IsConnectedAsync(ulong guildId) =>
        Task.FromResult(_guildStates.TryGetValue(guildId, out var state) && state.Connection is not null);

    public Task<QueueInfo?> GetQueueAsync(ulong guildId)
    {
        if (!_guildStates.TryGetValue(guildId, out var state))
            return Task.FromResult<QueueInfo?>(null);

        var current = state.CurrentTrackUrl is not null
            ? new TrackInfo { Title = state.CurrentTrackUrl, Artist = "Unknown", Duration = TimeSpan.Zero }
            : null;

        var queued = state.TrackQueue
            .Select(url => new TrackInfo { Title = url, Artist = "Unknown", Duration = TimeSpan.Zero })
            .ToList();

        return Task.FromResult<QueueInfo?>(new QueueInfo
        {
            CurrentTrack = current,
            QueuedTracks = queued
        });
    }

    private static async Task DisconnectAsync(GuildVoiceState state)
    {
        if (state.Connection is null) return;

        await state.Connection.DisposeAsync();
        state.Connection = null;
        state.ChannelId = null;
    }

    private async Task StreamAudioAsync(
        ulong guildId,
        GuildVoiceState state,
        string startUrl,
        CancellationToken sessionCt)
    {
        AudioWriter? audioWriter = null;
        try
        {
            audioWriter = state.Connection!.CreateAudioWriter(AudioFormat.S16LE48KHzStereoPCM);
            string? currentUrl = startUrl;
            while (currentUrl is not null && !sessionCt.IsCancellationRequested)
            {
                var trackSkipCts = new CancellationTokenSource();

                await state.Lock.WaitAsync(CancellationToken.None);
                state.TrackSkipCts = trackSkipCts;
                state.Lock.Release();

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCt, trackSkipCts.Token);

                try
                {
                    await StreamSingleTrackAsync(guildId, state, audioWriter, currentUrl, linkedCts.Token);
                }
                catch (OperationCanceledException) when (trackSkipCts.IsCancellationRequested && !sessionCt.IsCancellationRequested)
                {
                    _logger.LogInformation("Track skipped in guild {GuildId}", guildId);
                }

                await state.Lock.WaitAsync(CancellationToken.None);
                try
                {
                    state.TrackSkipCts = null;
                    state.CurrentTrackUrl = null;
                    currentUrl = state.TrackQueue.Count > 0 ? state.TrackQueue.Dequeue() : null;
                    if (currentUrl is not null)
                        state.CurrentTrackUrl = currentUrl;
                }
                finally
                {
                    state.Lock.Release();
                }
            }

            _logger.LogInformation("Playback finished for guild {GuildId}", guildId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Playback cancelled for guild {GuildId}", guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during playback for guild {GuildId}", guildId);
        }
        finally
        {
            audioWriter?.SignalSilence();

            await state.Lock.WaitAsync(CancellationToken.None);
            try
            {
                state.TrackSkipCts = null;
                state.CurrentTrackUrl = null;
                state.TrackQueue.Clear();
                state.PlaybackCts?.Dispose();
                state.PlaybackCts = null;
            }
            finally
            {
                state.Lock.Release();
            }
        }
    }

    private async Task StreamSingleTrackAsync(
        ulong guildId,
        GuildVoiceState state,
        AudioWriter audioWriter,
        string url,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = string.Join(" ",
                    "-loglevel error",
                    $"-i \"{url}\"",
                    "-ac 2",
                    "-f s16le",
                    "-ar 48000",
                    "pipe:1"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        byte[] buffer = new byte[65536];
        Stream audioStream = audioWriter.AsStream();
        Stream ffmpegStream = process.StandardOutput.BaseStream;
        long totalBytesWritten = 0;
        var writeStart = DateTimeOffset.UtcNow;

        try
        {
            int bytesRead;
            while ((bytesRead = await ffmpegStream.ReadAsync(buffer, ct)) > 0)
            {
                await MixTtsAsync(buffer, bytesRead, state);

                audioStream.Write(buffer, 0, bytesRead);
                totalBytesWritten += bytesRead;

                // Throttle writes: don't get more than MaxAheadBytes ahead of the estimated
                // playback position. This keeps the pre-fill small so that skip/stop take
                // effect promptly (within ~1 second) rather than draining a large buffer.
                long estimatedConsumedBytes = (long)((DateTimeOffset.UtcNow - writeStart).TotalSeconds * 192_000);
                long aheadBytes = totalBytesWritten - estimatedConsumedBytes;
                if (aheadBytes > MaxAheadBytes)
                {
                    double waitMs = (double)(aheadBytes - MaxAheadBytes) / 192.0;
                    await Task.Delay(Math.Max(1, (int)waitMs), ct);
                }
            }
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();

            string stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogWarning("[Voice] ffmpeg stderr for guild {GuildId}: {Stderr}", guildId, stderr);
        }
    }

    // Mixes pending TTS PCM in-place into the music buffer. The music is ducked
    // (MusicDuckFactor) and TTS is amplified (TtsAmplification) so speech is audible over
    // the track. Both streams are S16LE stereo 48kHz, so we mix at the short level.
    private async Task MixTtsAsync(byte[] musicBuffer, int count, GuildVoiceState state)
    {
        byte[]? ttsData;
        int ttsOffset;

        await state.Lock.WaitAsync();
        ttsData = state.TtsPcmData;
        ttsOffset = state.TtsPcmOffset;
        state.Lock.Release();

        if (ttsData is null) return;

        int ttsRemaining = ttsData.Length - ttsOffset;
        if (ttsRemaining <= 0)
        {
            await state.Lock.WaitAsync();
            if (state.TtsPcmData == ttsData) state.TtsPcmData = null;
            state.Lock.Release();
            return;
        }

        // Align to 2-byte (short) boundary; S16LE samples are always at least 2-byte aligned.
        int alignedCount = count & ~1;
        Span<short> musicSamples = MemoryMarshal.Cast<byte, short>(musicBuffer.AsSpan(0, alignedCount));

        int mixShorts = Math.Min(musicSamples.Length, ttsRemaining / 2);
        ReadOnlySpan<short> ttsSamples = MemoryMarshal.Cast<byte, short>(ttsData.AsSpan(ttsOffset, mixShorts * 2));

        for (int i = 0; i < mixShorts; i++)
        {
            float mixed = musicSamples[i] * MusicDuckFactor + ttsSamples[i] * TtsAmplification;
            musicSamples[i] = (short)Math.Clamp((int)mixed, short.MinValue, short.MaxValue);
        }

        int newOffset = ttsOffset + mixShorts * 2;

        await state.Lock.WaitAsync();
        if (state.TtsPcmData == ttsData)
        {
            state.TtsPcmOffset = newOffset;
            if (newOffset >= ttsData.Length)
                state.TtsPcmData = null;
        }
        state.Lock.Release();
    }

    // Runs ffmpeg on the given URL and returns the complete raw S16LE stereo 48kHz PCM.
    // Used only for short TTS clips where loading into memory is acceptable.
    private async Task<byte[]?> LoadPcmAsync(string url, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = string.Join(" ",
                    "-loglevel error",
                    $"-i \"{url}\"",
                    "-ac 2",
                    "-f s16le",
                    "-ar 48000",
                    "pipe:1"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            using var ms = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();

            string stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogWarning("[Voice] TTS ffmpeg stderr: {Stderr}", stderr);
        }
    }

    // Called by DSharpPlus when the voice connection is unexpectedly severed.
    // The handler is awaited BEFORE DisposeAsync(), so we must not block it: we snapshot
    // the current playback state, cancel the write loop, and fire-and-forget a reconnect
    // task that awaits the old StreamAudioAsync cleanup before rejoining.
    private async Task HandleVoiceDisconnectAsync(VoiceDisconnectReason reason, object? handlerState)
    {
        // Don't reconnect when we explicitly disconnected or the channel was deleted.
        if (reason is VoiceDisconnectReason.Disconnected or VoiceDisconnectReason.CallTerminated)
            return;

        var (guildId, channelId) = ((ulong, ulong))handlerState!;

        if (!_guildStates.TryGetValue(guildId, out var state)) return;

        _logger.LogWarning(
            "Voice disconnected in guild {GuildId} ({Reason}), scheduling reconnect",
            guildId, reason);

        string? currentUrl;
        string[] queuedUrls;
        Task? oldPlaybackTask;

        await state.Lock.WaitAsync();
        try
        {
            await CancelPlaybackAsync(state);

            // Mark connection as gone — DisposeAsync() will run on the old object after we return.
            state.Connection = null;
            state.ChannelId = null;

            currentUrl = state.CurrentTrackUrl;
            queuedUrls = [..state.TrackQueue];
            state.CurrentTrackUrl = null;
            state.TrackQueue.Clear();
            oldPlaybackTask = state.PlaybackTask;
        }
        finally
        {
            state.Lock.Release();
        }

        // Background task: wait for old StreamAudioAsync to finish its finally-block cleanup,
        // then wait for DisposeAsync() (which POSTs voiceChannelId=null) to complete,
        // then rejoin and restore playback.
        _ = Task.Run(async () =>
        {
            // Await old playback task so its finally block won't race with our state restore.
            if (oldPlaybackTask is not null)
                await oldPlaybackTask.ConfigureAwait(false);

            // Give DisposeAsync's REST call time to complete so we don't race its
            // ModifyGuildMember(voiceChannelId=null) against our ConnectAsync.
            int delayMs = reason == VoiceDisconnectReason.Ratelimited ? 10_000 : 3_000;
            await Task.Delay(delayMs);

            _logger.LogInformation(
                "Reconnecting to voice channel {ChannelId} in guild {GuildId}",
                channelId, guildId);

            bool joined = await JoinVoiceChannelAsync(guildId, channelId);
            if (!joined)
            {
                _logger.LogError(
                    "Failed to reconnect to voice channel {ChannelId} in guild {GuildId}",
                    channelId, guildId);
                return;
            }

            if (currentUrl is null) return;

            // Restore queue and restart playback from the beginning of the interrupted track.
            await state.Lock.WaitAsync();
            try
            {
                if (state.Connection is null) return;

                foreach (string url in queuedUrls)
                    state.TrackQueue.Enqueue(url);

                state.CurrentTrackUrl = currentUrl;
                state.PlaybackCts = new CancellationTokenSource();
                state.PlaybackTask = Task.Run(() =>
                    StreamAudioAsync(guildId, state, currentUrl, state.PlaybackCts!.Token));
            }
            finally
            {
                state.Lock.Release();
            }

            _logger.LogInformation("Resumed playback after reconnect in guild {GuildId}", guildId);
        });
    }

    private static async Task CancelPlaybackAsync(GuildVoiceState state)
    {
        if (state.PlaybackCts is null) return;

        await state.PlaybackCts.CancelAsync();
        state.PlaybackCts.Dispose();
        state.PlaybackCts = null;
    }
}
