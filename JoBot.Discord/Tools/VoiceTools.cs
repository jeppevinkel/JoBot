using JoBot.Core.Attributes;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;

namespace JoBot.Discord.Tools;

public class VoiceTools : IToolProvider
{
    private readonly IVoiceService _voiceService;

    public VoiceTools(IVoiceService voiceService)
    {
        _voiceService = voiceService;
    }

    [AiTool("Join a voice channel")]
    public async Task<string> JoinVoiceChannelAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("Voice channel ID")] ulong channelId)
    {
        var success = await _voiceService.JoinVoiceChannelAsync(guildId, channelId);
        return success ? ToolResult.Success("Joined voice channel.") : ToolResult.Failure("Failed to join voice channel.");
    }

    [AiTool("Leave the current voice channel")]
    public async Task<string> LeaveVoiceChannelAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        await _voiceService.LeaveVoiceChannelAsync(guildId);
        return ToolResult.Success("Left voice channel.");
    }

    [AiTool("Play audio from a stream URL in the currently connected voice channel")]
    public async Task<string> PlayAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("The stream URL of the audio to play")] string streamUrl)
    {
        var success = await _voiceService.PlayAsync(guildId, streamUrl);
        return success
            ? ToolResult.Success("Playback started.")
            : ToolResult.Failure("Failed to start playback. Ensure the bot is connected to a voice channel first.");
    }

    [AiTool("Stop audio playback in the current voice channel")]
    public async Task<string> StopAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        await _voiceService.StopAsync(guildId);
        return ToolResult.Success("Playback stopped.");
    }

    [AiTool("Check if the bot is connected to a voice channel")]
    public async Task<string> IsConnectedAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var connected = await _voiceService.IsConnectedAsync(guildId);
        return ToolResult.Success(new { connected });
    }

    [AiTool("Check if audio is currently playing")]
    public async Task<string> IsPlayingAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var playing = await _voiceService.IsPlayingAsync(guildId);
        return ToolResult.Success(new { playing });
    }

    [AiTool("Get the current queue")]
    public async Task<string> GetQueueAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var queue = await _voiceService.GetQueueAsync(guildId);

        return queue is null
            ? ToolResult.Failure("Not connected to voice.")
            : ToolResult.Success(queue);
    }

    [AiTool("Add a song to the queue")]
    public async Task<string> EnqueueAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("Stream URL of the track")] string streamUrl)
    {
        var success = await _voiceService.EnqueueAsync(guildId, streamUrl);
        return success
            ? ToolResult.Success("Track added to queue.")
            : ToolResult.Failure("Failed to add track to queue.");
    }

    [AiTool("Skip the current track")]
    public async Task<string> SkipAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        await _voiceService.SkipAsync(guildId);
        return ToolResult.Success("Skipped current track.");
    }
}