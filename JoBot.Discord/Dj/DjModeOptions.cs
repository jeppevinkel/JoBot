namespace JoBot.Discord.Dj;

public class DjModeOptions
{
    public const string SectionName = "DjMode";

    /// <summary>Offer DJ mode to the AI. When false, the <c>set_dj_mode</c> tool isn't available.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The AI is asked to add tracks once this many or fewer are left in the queue.</summary>
    public int LowQueueThreshold { get; init; } = 2;

    /// <summary>How many tracks the AI is asked to add each time.</summary>
    public int TopUpCount { get; init; } = 5;

    /// <summary>Minimum time between asking the AI for more tracks.</summary>
    public TimeSpan NudgeCooldown { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// After this many top-ups in a row where the AI added nothing, DJ mode pauses until someone turns it on again.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>How many recently played and skipped tracks are passed to the AI, so it can avoid repeats.</summary>
    public int HistorySize { get; init; } = 50;

    /// <summary>Most times per hour a guild's AI is asked for more tracks, to keep costs bounded.</summary>
    public int MaxNudgesPerHour { get; init; } = 20;

    /// <summary>How often queues are checked.</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(5);
}
