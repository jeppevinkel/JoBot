using JoBot.Core.Actions;
using JoBot.Core.Models;

namespace JoBot.Core.Interfaces;

public interface IAiService
{
    IAsyncEnumerable<AiAction> ProcessAsync(
        ulong guildId,
        MessagePayload payload,
        CancellationToken cancellationToken = default);
}