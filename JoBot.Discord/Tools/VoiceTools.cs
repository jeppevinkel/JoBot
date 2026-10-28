using JoBot.Core.Attributes;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Dj;

namespace JoBot.Discord.Tools;

public class VoiceTools : IToolProvider
{
    private readonly IVoiceService _voiceService;
    private readonly DjSessionStore _djSessions;

    public VoiceTools(IVoiceService voiceService, DjSessionStore djSessions)
    {
        _voiceService = voiceService;
        _djSessions = djSessions;
    }

    [AiTool("Join a voice channel")]
    public async Task<string> JoinVoiceChannelAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("Voice channel ID")] ulong channelId)
    {
        var success = await _voiceService.JoinVoiceChannelAsync(guildId, channelId);
        return success ? ToolResult.Success("Joined voice channel.") : ToolResult.Failure("Failed to join voice channel.");
    }

    [AiTool("Leave the current voice channel. Music stops right away and DJ mode turns off; anything you say before or after this is still spoken before leaving")]
    public async Task<string> LeaveVoiceChannelAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        // Otherwise DJ mode could refill the queue before the bot is gone
        _djSessions.Disable(guildId);
        await _voiceService.LeaveVoiceChannelAsync(guildId);
        return ToolResult.Success("Leaving the voice channel once any speech has finished.");
    }

    // [AiTool("Play audio from a stream URL in the currently connected voice channel")]
    // public async Task<string> PlayAsync(
    //     [AiParameter("Guild ID")] ulong guildId,
    //     [AiParameter("The stream URL of the audio to play")] string streamUrl)
    // {
    //     var success = await _voiceService.PlayAsync(guildId, streamUrl);
    //     return success
    //         ? ToolResult.Success("Playback started.")
    //         : ToolResult.Failure("Failed to start playback. Ensure the bot is connected to a voice channel first.");
    // }

    [AiTool("Play TTS audio from a stream URL in the currently connected voice channel (this temporarily interrupts any ongoing audio playback)")]
    public async Task<string> PlayTtsAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("The stream URL of the audio to play")] string streamUrl)
    {
        var success = await _voiceService.PlayTtsAsync(guildId, streamUrl);
        return success
            ? ToolResult.Success("Playback started.")
            : ToolResult.Failure("Failed to start playback. Ensure the bot is connected to a voice channel first.");
    }

    [AiTool("Stop audio playback in the current voice channel. This also turns DJ mode off")]
    public async Task<string> StopAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var wasDjMode = _djSessions.Disable(guildId);
        await _voiceService.StopAsync(guildId);
        return ToolResult.Success(wasDjMode ? "Playback stopped and DJ mode turned off." : "Playback stopped.");
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
        [AiParameter("Stream URL of the track")] string streamUrl,
        [AiParameter("Track title to display in the queue", Required = false)] string? title = null,
        [AiParameter("Track artist to display in the queue", Required = false)] string? artist = null,
        [AiParameter("Track album to display in the queue", Required = false)] string? album = null)
    {
        // Tracks queued while topping up for DJ mode go behind the ones people ask for
        DjTurn? djTurn = DjTurn.Current;
        var success = await _voiceService.EnqueueAsync(guildId, streamUrl, title, artist, album, isDjPick: djTurn is not null);
        if (success)
            djTurn?.RecordTrackAdded();

        return success
            ? ToolResult.Success("Track added to queue.")
            : ToolResult.Failure("Failed to add track to queue.");
    }

    [AiTool("Skip the current track")]
    public async Task<string> SkipAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        // DJ mode steers away from what people skip
        if (_djSessions.Get(guildId) is { } djSession &&
            await _voiceService.GetNowPlayingAsync(guildId) is { Track: { } skipped })
            djSession.RecordSkip(skipped);

        await _voiceService.SkipAsync(guildId);
        return ToolResult.Success("Skipped current track.");
    }

    [AiTool("Get the music track that is currently playing, including its playback position and whether it is paused")]
    public async Task<string> GetNowPlayingAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var nowPlaying = await _voiceService.GetNowPlayingAsync(guildId);
        if (nowPlaying is null)
            return ToolResult.Failure("Not connected to voice.");

        return nowPlaying.Track is null
            ? ToolResult.Success("Nothing is playing.")
            : ToolResult.Success(nowPlaying);
    }

    [AiTool("Pause music playback. Unlike stopping, the current track and queue are kept and can be resumed")]
    public async Task<string> PauseAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var result = await _voiceService.PauseAsync(guildId);
        return ToResultMessage(result, "Playback paused.");
    }

    [AiTool("Resume paused music playback")]
    public async Task<string> ResumeAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var result = await _voiceService.ResumeAsync(guildId);
        return ToResultMessage(result, "Playback resumed.");
    }

    [AiTool("Seek to a position in the current track")]
    public async Task<string> SeekAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("Position to seek to, in seconds from the start of the track")] int seconds)
    {
        var result = await _voiceService.SeekAsync(guildId, TimeSpan.FromSeconds(seconds));
        return ToResultMessage(result, $"Seeked to {TimeSpan.FromSeconds(seconds):g}.",
            invalidArgumentMessage: "Position must be between 0 and the duration of the current track.");
    }

    [AiTool("Get the current music volume as a percentage")]
    public async Task<string> GetVolumeAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var volume = await _voiceService.GetVolumeAsync(guildId);
        return volume is null
            ? ToolResult.Failure("Not connected to voice.")
            : ToolResult.Success(new { volume = (int)Math.Round(volume.Value * 100) });
    }

    [AiTool("Set the music volume for the current session. This does not change the server's default volume")]
    public async Task<string> SetVolumeAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("Volume as a percentage from 0 to 100")] int volume)
    {
        var result = await _voiceService.SetVolumeAsync(guildId, volume / 100f);
        return ToResultMessage(result, $"Volume set to {volume}%.",
            invalidArgumentMessage: "Volume must be between 0 and 100.");
    }

    [AiTool("Remove a track from the queue without affecting the current track. Use get_queue first to find the track's position")]
    public async Task<string> RemoveFromQueueAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("Position of the track in the queue, as returned by get_queue (1 is the next track)")] int position)
    {
        var (result, track) = await _voiceService.RemoveFromQueueAsync(guildId, position - 1);
        return ToResultMessage(result, $"Removed \"{track?.Title}\" by {track?.Artist} from the queue.",
            invalidArgumentMessage: "There is no track at that position. Use get_queue to check the current positions.");
    }

    [AiTool("Move a track to a different position in the queue. Use get_queue first to find the track's position")]
    public async Task<string> MoveQueueTrackAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("Current position of the track in the queue, as returned by get_queue (1 is the next track)")] int fromPosition,
        [AiParameter("New position for the track (1 plays it next); positions past the end move it to the end")] int toPosition)
    {
        var (result, track) = await _voiceService.MoveQueueTrackAsync(guildId, fromPosition - 1, toPosition - 1);
        return ToResultMessage(result, $"Moved \"{track?.Title}\" by {track?.Artist} in the queue.",
            invalidArgumentMessage: "There is no track at that position, or the new position is invalid. Use get_queue to check the current positions.");
    }

    [AiTool("Remove all upcoming tracks from the queue. The current track keeps playing. Only use this when explicitly asked to clear the queue")]
    public async Task<string> ClearQueueAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var removed = await _voiceService.ClearQueueAsync(guildId);
        return removed is null
            ? ToolResult.Failure("Not connected to voice.")
            : ToolResult.Success($"Removed {removed} track(s) from the queue.");
    }

    [AiTool("List the voice channels in a guild, including how many users are in each and whether the bot can join. Use this to find a channel ID when the user names a channel instead of giving an ID")]
    public async Task<string> GetVoiceChannelsAsync(
        [AiParameter("Guild ID")] ulong guildId)
    {
        var channels = await _voiceService.GetVoiceChannelsAsync(guildId);
        return channels is null
            ? ToolResult.Failure("Guild not found.")
            : ToolResult.Success(channels);
    }

    private static string ToResultMessage(
        PlaybackResult result,
        string successMessage,
        string invalidArgumentMessage = "Invalid argument.") => result switch
        {
            PlaybackResult.Success => ToolResult.Success(successMessage),
            PlaybackResult.NotConnected => ToolResult.Failure("Not connected to voice."),
            PlaybackResult.NothingPlaying => ToolResult.Failure("Nothing is playing."),
            PlaybackResult.NotSeekable => ToolResult.Failure("The current track does not support seeking."),
            PlaybackResult.InvalidArgument => ToolResult.Failure(invalidArgumentMessage),
            _ => ToolResult.Failure("Something went wrong. Check that the bot is connected and try again.")
        };
}