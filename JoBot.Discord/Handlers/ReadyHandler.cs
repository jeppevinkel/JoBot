using DSharpPlus;
using DSharpPlus.EventArgs;

namespace JoBot.Discord.Handlers;

public class ReadyHandler : IEventHandler<GuildDownloadCompletedEventArgs>
{
    public Task HandleEventAsync(DiscordClient client, GuildDownloadCompletedEventArgs eventArgs)
    {
        Console.WriteLine($"Ready event received for guilds: {string.Join(", ", eventArgs.Guilds.Values.Select(g => g.Name))}");
        return Task.CompletedTask;
    }
}