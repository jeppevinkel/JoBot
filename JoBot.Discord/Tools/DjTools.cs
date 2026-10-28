using JoBot.Core.Attributes;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;
using JoBot.Discord.Dj;
using Microsoft.Extensions.Options;

namespace JoBot.Discord.Tools;

public class DjTools : IToolProvider
{
    private readonly IVoiceService _voiceService;
    private readonly DjSessionStore _sessions;
    private readonly DjModeOptions _options;

    public DjTools(IVoiceService voiceService, DjSessionStore sessions, IOptions<DjModeOptions> options)
    {
        _voiceService = voiceService;
        _sessions = sessions;
        _options = options.Value;
    }

    [AiTool("Turn DJ mode on or off. While it's on, you're asked to add more tracks whenever the queue runs low, so " +
            "the music keeps going without anyone requesting songs. Only turn it on when someone asks for it. To " +
            "change the guidance or announcements while it's on, call this again with enabled set to true. The bot " +
            "must be in a voice channel")]
    public async Task<string> SetDjModeAsync(
        [AiParameter("Guild ID")] ulong guildId,
        [AiParameter("Turn DJ mode on (true) or off (false)")] bool enabled,
        [AiParameter("ID of the channel the request came from. Announcements and notices about DJ mode are posted " +
                     "there. Required when turning DJ mode on", Required = false)] ulong? channelId = null,
        [AiParameter("Style or mood notes from the user, e.g. \"90s hip hop, keep energy high\". Leave out to keep " +
                     "the current guidance", Required = false)] string? guidance = null,
        [AiParameter("Whether to announce added tracks out loud in the voice channel and in chat, like a radio DJ. " +
                     "Only turn this on when asked. Leave out to keep the current setting (off when turning DJ mode " +
                     "on)", Required = false)] bool? announce = null)
    {
        if (!enabled)
        {
            return _sessions.Disable(guildId)
                ? ToolResult.Success(new { enabled = false })
                : ToolResult.Success("DJ mode was already off.");
        }

        if (!await _voiceService.IsConnectedAsync(guildId))
            return ToolResult.Failure("The bot isn't in a voice channel. Join one first.");

        ulong? targetChannelId = channelId ?? _sessions.Get(guildId)?.Status.ChannelId;
        if (targetChannelId is null)
            return ToolResult.Failure("channelId is required when turning DJ mode on.");

        DjStatus status = _sessions.Enable(guildId, targetChannelId.Value, guidance, announce);

        return ToolResult.Success(new
        {
            enabled = true,
            guidance = status.Guidance,
            announce = status.Announce,
            note = $"You'll be asked to add tracks whenever {_options.LowQueueThreshold} or fewer are left in the " +
                   "queue, within a few seconds if it's already that short, so there's no need to fill the queue now."
        });
    }
}
