using DSharpPlus;
using DSharpPlus.EventArgs;
using JoBot.Core.Actions;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Builders;
using JoBot.Discord.Helpers;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Handlers;

public class MessageHandler : IEventHandler<MessageCreatedEventArgs>
{
    private readonly ILogger<MessageHandler> _logger;
    private readonly IMessagePayloadBuilder _payloadBuilder;
    private readonly IAiService _aiService;

    public MessageHandler(ILogger<MessageHandler> logger, IMessagePayloadBuilder payloadBuilder, IAiService aiService)
    {
        _logger = logger;
        _payloadBuilder = payloadBuilder;
        _aiService = aiService;
    }

    public async Task HandleEventAsync(DiscordClient client, MessageCreatedEventArgs eventArgs)
    {
        if (eventArgs.Author.IsBot) return;

        bool isBotMentioned = eventArgs.MentionedUsers.Any(u => u.IsCurrent);
        bool isReplyToBot = eventArgs.Message.ReferencedMessage?.Author?.IsCurrent ?? false;
        bool isDirectMessage = eventArgs.Channel.IsPrivate;

        if (!isBotMentioned && !isReplyToBot && !isDirectMessage) return;

        if (isDirectMessage)
        {
            await eventArgs.Message.RespondAsync("Why are you sending me direct messages? (P.s. I can't respond to direct messages.)");
            return;
        }

        MessagePayload payload = await _payloadBuilder.BuildAsync(eventArgs);

        await foreach (AiAction action in _aiService.ProcessAsync(eventArgs.Guild.Id, payload))
        {
            switch (action)
            {
                case ReplyAction reply:
                    var replyChunks = MessageChunker.Split(reply.Content).ToList();
                    for (var i = 0; i < replyChunks.Count; i++)
                    {
                        if (i == 0)
                            await eventArgs.Message.RespondAsync(replyChunks[i]);
                        else
                            await eventArgs.Channel.SendMessageAsync(replyChunks[i]);
                    }
                    break;
                case RespondAction response:
                    foreach (var chunk in MessageChunker.Split(response.Content))
                        await eventArgs.Channel.SendMessageAsync(chunk);
                    break;
                case IgnoreAction:
                    _logger.LogDebug("AI chose to ignore message {MessageId}", payload.Message.Id);
                    break;
            }
        }
    }
}