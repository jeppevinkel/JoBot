using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.TextCommands.Parsing;
using DSharpPlus.Entities;

namespace JoBot.Discord.Resolvers;

public class NeverMatchPrefixResolver : IPrefixResolver
{
    public ValueTask<int> ResolvePrefixAsync(CommandsExtension extension, DiscordMessage message)
        => ValueTask.FromResult(-1); // -1 = no match
}