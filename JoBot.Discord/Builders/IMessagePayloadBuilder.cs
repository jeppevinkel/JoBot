using DSharpPlus.EventArgs;
using JoBot.Core.Models;

namespace JoBot.Discord.Builders;

public interface IMessagePayloadBuilder
{
    Task<MessagePayload> BuildAsync(MessageCreatedEventArgs eventArgs);
}