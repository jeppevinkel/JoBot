namespace JoBot.Discord.Voice;

/// <summary>Tracks how long the bot has been alone in a voice channel, per guild.</summary>
public sealed class AloneTracker
{
    private readonly Dictionary<ulong, DateTimeOffset> _aloneSince = new();

    /// <summary>Records whether the bot is currently alone in <paramref name="guildId"/>.</summary>
    /// <returns>True once the bot has been alone for at least <paramref name="timeout"/>.</returns>
    public bool Update(ulong guildId, bool isAlone, DateTimeOffset now, TimeSpan timeout)
    {
        if (!isAlone)
        {
            _aloneSince.Remove(guildId);
            return false;
        }

        if (!_aloneSince.TryGetValue(guildId, out DateTimeOffset since))
        {
            _aloneSince[guildId] = now;
            return false;
        }

        return now - since >= timeout;
    }

    /// <summary>Stops tracking a guild, e.g. because the bot left its voice channel.</summary>
    public void Forget(ulong guildId) => _aloneSince.Remove(guildId);
}
