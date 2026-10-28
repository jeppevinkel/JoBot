using JoBot.Core.Actions;
using JoBot.Core.Models;

namespace JoBot.Core.Interfaces;

public interface IAiService
{
    IAsyncEnumerable<AiAction> ProcessAsync(
        ulong guildId,
        MessagePayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets the AI act on something the bot noticed by itself. The AI sees the guild's conversation for context,
    /// but the event and how it was handled aren't added to it, so automated events can't crowd out what people said.
    /// </summary>
    /// <param name="buildPayload">
    /// Builds the event once it's the guild's turn; messages to the AI are handled one at a time per guild, so
    /// things may have changed while waiting. Returns null if there's no longer anything to do.
    /// </param>
    IAsyncEnumerable<AiAction> ProcessEventAsync(
        ulong guildId,
        Func<CancellationToken, Task<AutomatedEventPayload?>> buildPayload,
        CancellationToken cancellationToken = default);
}
