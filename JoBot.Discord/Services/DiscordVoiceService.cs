// using System.Collections.Concurrent;
// using System.Diagnostics;
// using DSharpPlus;
// using DSharpPlus.Entities;
// using DSharpPlus.Voice;
// using JoBot.Core.Interfaces;
// using JoBot.Discord.Extensions;
// using Microsoft.Extensions.Logging;
//
// namespace JoBot.Discord.Services;
//
// public class DiscordVoiceService : IVoiceService
// {
//     private readonly ILogger<DiscordVoiceService> _logger;
//     private readonly DiscordClient _discordClient;
//     private readonly ConcurrentDictionary<ulong, GuildVoiceState> _guildStates = new();
//     
//     public DiscordVoiceService(
//         ILogger<DiscordVoiceService> logger,
//         DiscordClient discordClient)
//     {
//         _logger = logger;
//         _discordClient = discordClient;
//     }
//     
//     public async Task<bool> JoinVoiceChannelAsync(ulong guildId, ulong channelId)
//     {
//         GuildVoiceState state = _guildStates.GetOrAdd(guildId, static _ => new GuildVoiceState());
//         
//         await state.Lock.WaitAsync();
//         try
//         {
//             // Already connected - disconnect first before joining new channel
//             if (state.Connection is not null)
//             {
//                 _logger.LogInformation(
//                     "Already connected to channel {ChannelId} in guild {GuildId}, disconnecting first",
//                     state.ChannelId, guildId);
//
//                 await DisconnectAsync(state);
//             }
//             
//             var (guild, reason) = await _discordClient.TryGetGuildWithReasonAsync(guildId, _logger);
//             if (guild is null)
//             {
//                 _logger.LogWarning("Could not find guild {GuildId}: {Reason}", guildId, reason);
//                 return false;
//             }
//
//             DiscordChannel channel;
//             try
//             {
//                 channel = await guild.GetChannelAsync(channelId);
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogError(ex, "Failed to get channel {ChannelId} in guild {GuildId}", channelId, guildId);
//                 return false;
//             }
//
//             if (channel.Type != DiscordChannelType.Voice)
//             {
//                 _logger.LogWarning("Channel {ChannelId} in guild {GuildId} is not a voice channel", channelId, guildId);
//                 return false;
//             }
//
//             state.Connection = await channel.ConnectAsync();
//             state.ChannelId = channelId;
//
//             _logger.LogInformation("Joined voice channel {ChannelId} in guild {GuildId}", channelId, guildId);
//             return true;
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error joining voice channel {ChannelId} in guild {GuildId}", channelId, guildId);
//             return false;
//         }
//         finally
//         {
//             state.Lock.Release();
//         }
//     }
//
//     public async Task LeaveVoiceChannelAsync(ulong guildId)
//     {
//         if (!_guildStates.TryGetValue(guildId, out var state))
//         {
//             _logger.LogDebug("No voice state found for guild {GuildId}", guildId);
//             return;
//         }
//
//         await state.Lock.WaitAsync();
//         try
//         {
//             if (state.Connection is null)
//             {
//                 _logger.LogDebug("Not connected to voice in guild {GuildId}", guildId);
//                 return;
//             }
//
//             await DisconnectAsync(state);
//             _logger.LogInformation("Left voice channel in guild {GuildId}", guildId);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error leaving voice channel in guild {GuildId}", guildId);
//         }
//         finally
//         {
//             state.Lock.Release();
//         }
//     }
//     
//     public async Task<bool> PlayAsync(ulong guildId, string streamUrl)
//     {
//         if (!_guildStates.TryGetValue(guildId, out var state))
//         {
//             _logger.LogWarning("No voice state found for guild {GuildId}", guildId);
//             return false;
//         }
//
//         await state.Lock.WaitAsync();
//         try
//         {
//             if (state.Connection is null)
//             {
//                 _logger.LogWarning("Not connected to voice in guild {GuildId}", guildId);
//                 return false;
//             }
//
//             // Stop existing playback if any
//             if (state.IsPlaying)
//                 await CancelPlaybackAsync(state);
//
//             state.PlaybackCts = new CancellationTokenSource();
//         }
//         finally
//         {
//             state.Lock.Release();
//         }
//
//         // Start playback in the background so tool call returns immediately
//         _ = Task.Run(() => StreamAudioAsync(guildId, state, streamUrl, state.PlaybackCts.Token));
//         return true;
//     }
//     
//     public async Task StopAsync(ulong guildId)
//     {
//         if (!_guildStates.TryGetValue(guildId, out var state)) return;
//
//         await state.Lock.WaitAsync();
//         try
//         {
//             await CancelPlaybackAsync(state);
//         }
//         finally
//         {
//             state.Lock.Release();
//         }
//     }
//
//     public Task<bool> IsPlayingAsync(ulong guildId)
//     {
//         return Task.FromResult(IsPlaying(guildId));
//     }
//     
//     public Task<bool> IsConnectedAsync(ulong guildId)
//     {
//         return Task.FromResult(IsConnected(guildId));
//     }
//
//     public bool IsPlaying(ulong guildId) =>
//         _guildStates.TryGetValue(guildId, out var state) && state.IsPlaying;
//
//     public bool IsConnected(ulong guildId) =>
//         _guildStates.TryGetValue(guildId, out var state) && state.Connection is not null;
//
//     private static async Task DisconnectAsync(GuildVoiceState state)
//     {
//         if (state.Connection is null) return;
//
//         // await state.Connection.DisconnectAsync();
//         await state.Connection.DisposeAsync();
//         state.Connection = null;
//         state.ChannelId = null;
//     }
//     
//     private async Task StreamAudioAsync(
//         ulong guildId,
//         GuildVoiceState state,
//         string streamUrl,
//         CancellationToken ct)
//     {
//         AudioWriter audioWriter = state.Connection!.CreateAudioWriter(AudioFormat.S16LE48KHzMonoPCM);
//         Stream audioStream = audioWriter.AsStream();
//
//         using var process = new Process
//         {
//             StartInfo = new ProcessStartInfo
//             {
//                 FileName = "ffmpeg",
//                 Arguments = string.Join(" ",
//                     "-loglevel quiet",
//                     $"-i \"{streamUrl}\"",
//                     "-ac 1",        // mono
//                     "-f s16le",     // PCM 16-bit little endian
//                     "-ar 48000",    // 48kHz sample rate
//                     "pipe:1"),      // output to stdout
//                 RedirectStandardOutput = true,
//                 UseShellExecute = false,
//                 CreateNoWindow = true
//             }
//         };
//         
//         try
//         {
//             process.Start();
//
//             await process.StandardOutput.BaseStream.CopyToAsync(audioStream, ct);
//
//             await audioWriter.FlushAsync(ct);
//
//             _logger.LogInformation("Playback finished for guild {GuildId}", guildId);
//         }
//         catch (OperationCanceledException)
//         {
//             _logger.LogInformation("Playback cancelled for guild {GuildId}", guildId);
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error during playback for guild {GuildId}", guildId);
//         }
//         finally
//         {
//             // Always signal silence so Discord knows we've stopped sending audio
//             audioWriter.SignalSilence();
//
//             if (!process.HasExited)
//                 process.Kill();
//
//             await state.Lock.WaitAsync();
//             try
//             {
//                 state.PlaybackCts?.Dispose();
//                 state.PlaybackCts = null;
//             }
//             finally
//             {
//                 state.Lock.Release();
//             }
//         }
//     }
//     
//     private static async Task CancelPlaybackAsync(GuildVoiceState state)
//     {
//         if (state.PlaybackCts is null) return;
//
//         await state.PlaybackCts.CancelAsync();
//         state.PlaybackCts.Dispose();
//         state.PlaybackCts = null;
//     }
// }