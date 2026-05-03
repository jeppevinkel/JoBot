using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Services;

public class DiscordBotService : IHostedService
{
    private readonly ILogger<DiscordBotService> _logger;
    private readonly DiscordClient _discordClient;

    public DiscordBotService(
        ILogger<DiscordBotService> logger,
        DiscordClient discordClient)
    {
        _logger = logger;
        _discordClient = discordClient;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _discordClient.ConnectAsync(activity: new DiscordActivity("with the api", DiscordActivityType.Playing));
        _logger.LogInformation("Connected to Discord");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _discordClient.DisconnectAsync();
        _logger.LogInformation("Disconnected from Discord");
    }
}