using JoBot.Core.Actions;
using JoBot.Core.Models;

namespace JoBot.Core.Interfaces;

public interface IAiService
{
    /// <summary>
    /// Answers a message. Messages to the AI are answered one at a time per guild, except that a message arriving
    /// in a channel where someone is already being answered joins that answer at its next tool call; the caller
    /// then gets a single <see cref="MergedAction"/>, once the shared answer is done, and delivers nothing itself.
    /// </summary>
    IAsyncEnumerable<AiAction> ProcessAsync(
        ulong guildId,
        MessagePayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets the AI act on something the bot noticed by itself. The AI sees the guild's conversation for context,
    /// but the event and how it was handled aren't added to it, so automated events can't crowd out what people said.
    /// It waits for anyone already being answered, but doesn't hold up messages that arrive while it runs.
    /// </summary>
    /// <param name="buildPayload">
    /// Builds the event once anyone already being answered has been, since things may have changed while waiting.
    /// Returns null if there's no longer anything to do.
    /// </param>
    IAsyncEnumerable<AiAction> ProcessEventAsync(
        ulong guildId,
        Func<CancellationToken, Task<AutomatedEventPayload?>> buildPayload,
        CancellationToken cancellationToken = default);
}
