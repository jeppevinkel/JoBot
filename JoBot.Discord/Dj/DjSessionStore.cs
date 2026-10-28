using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace JoBot.Discord.Dj;

/// <summary>
/// Which guilds have DJ mode on. Tools turn it on and off here; <see cref="DjModeService"/> keeps their queues topped up.
/// </summary>
/// <remarks>Kept apart from <see cref="DjModeService"/>, which uses the AI service, since the AI service uses the tools.</remarks>
public sealed class DjSessionStore
{
    private readonly ConcurrentDictionary<ulong, DjSession> _sessions = new();
    private readonly DjModeOptions _options;

    public DjSessionStore(IOptions<DjModeOptions> options)
    {
        _options = options.Value;
    }

    public IEnumerable<KeyValuePair<ulong, DjSession>> All => _sessions;

    /// <summary>Turns DJ mode on, or updates its settings and resumes it if it's already on.</summary>
    /// <param name="channelId">Channel to post announcements and notices in.</param>
    /// <param name="guidance">Style notes, or null to keep the current ones.</param>
    /// <param name="announce">Whether to announce top-ups, or null to keep the current setting (off when turning on).</param>
    public DjStatus Enable(ulong guildId, ulong channelId, string? guidance, bool? announce) =>
        _sessions.GetOrAdd(guildId, static (_, historySize) => new DjSession(historySize), _options.HistorySize)
            .Update(channelId, guidance, announce);

    /// <returns>Whether DJ mode was on.</returns>
    public bool Disable(ulong guildId)
    {
        if (!_sessions.TryRemove(guildId, out DjSession? session))
            return false;

        session.End();
        return true;
    }

    /// <summary>Turns DJ mode off, but only if <paramref name="session"/> is still the guild's current session.</summary>
    public bool Disable(ulong guildId, DjSession session)
    {
        if (!_sessions.TryRemove(KeyValuePair.Create(guildId, session)))
            return false;

        session.End();
        return true;
    }

    public DjSession? Get(ulong guildId) => _sessions.GetValueOrDefault(guildId);

    /// <summary>Whether <paramref name="session"/> is still the guild's current session, i.e. DJ mode wasn't turned off.</summary>
    public bool IsCurrent(ulong guildId, DjSession session) =>
        _sessions.TryGetValue(guildId, out DjSession? current) && current == session;
}
