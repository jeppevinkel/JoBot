using DSharpPlus.Entities;

namespace JoBot.Discord.Resolvers;

public class DisplayNameResolver : IDisplayNameResolver
{
    public async Task<string> ResolveAsync(DiscordUser user, DiscordGuild? guild = null)
    {
        if (guild is not null)
        {
            var member = await guild.GetMemberAsync(user.Id);
            if (member?.Nickname is not null)
                return member.Nickname;
        }

        return user.GlobalName ?? user.Username;
    }

    public async Task<IReadOnlyDictionary<ulong, string>> ResolveManyAsync(
        IEnumerable<DiscordUser> users,
        DiscordGuild? guild = null)
    {
        var tasks = users.Select(async user =>
            (user.Id, Name: await ResolveAsync(user, guild)));

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(x => x.Id, x => x.Name);
    }
}