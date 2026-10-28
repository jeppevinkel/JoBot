namespace JoBot.Core.Actions;

/// <summary>
/// The message was added to a reply the AI was already working on for the same channel. That reply answers it,
/// and is delivered by whoever started it.
/// </summary>
public record MergedAction : AiAction;
