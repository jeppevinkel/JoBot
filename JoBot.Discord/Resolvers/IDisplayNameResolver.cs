using DSharpPlus.Entities;

namespace JoBot.Discord.Resolvers;

public interface IDisplayNameResolver
{
    Task<string> ResolveAsync(DiscordUser user, DiscordGuild? guild = null);
    Task<IReadOnlyDictionary<ulong, string>> ResolveManyAsync(IEnumerable<DiscordUser> users, DiscordGuild? guild = null);
}