using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using JoBot.Core.Actions;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Discord.Builders;
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

        MessagePayload payload = await _payloadBuilder.BuildAsync(eventArgs);

        await foreach (AiAction action in _aiService.ProcessAsync(eventArgs.Guild.Id, payload))
        {
            switch (action)
            {
                case ReplyAction reply:
                    await eventArgs.Message.RespondAsync(reply.Content);
                    break;
                case RespondAction response:
                    await eventArgs.Channel.SendMessageAsync(response.Content);
                    break;
                case IgnoreAction:
                    _logger.LogDebug("AI chose to ignore message {MessageId}", payload.Message.Id);
                    break;
            }
        }
    }
}