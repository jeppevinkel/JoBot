using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.InteractionNamingPolicies;
using DSharpPlus.Extensions;
using JoBot.Core.Interfaces;
using JoBot.Discord.Builders;
using JoBot.Discord.Commands;
using JoBot.Discord.Handlers;
using JoBot.Discord.Resolvers;
using JoBot.Discord.Services;
using JoBot.Discord.Tools;
using Lavalink4NET.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JoBot.Discord.Extensions;

public static class DiscordServiceExtensions
{
    public static IServiceCollection AddDiscordServices(this IServiceCollection services, IConfiguration config)
    {
        var token = config["Discord:Token"]
                    ?? throw new InvalidOperationException("Discord:Token is not configured.");

        services.AddHostedService<DiscordBotService>();
        services.AddDiscordClient(token, DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents);

        services.AddCommandsExtension((provider, extension) =>
        {
            SlashCommandProcessor slashCommandProcessor = new(new SlashCommandConfiguration()
            {
                NamingPolicy = new KebabCaseNamingPolicy(),
            });

            extension.AddCommands<SettingsCommands>();

            extension.AddProcessor(slashCommandProcessor);
        }, new CommandsConfiguration
        {
            DebugGuildId = 330295897638436864
        });

        services.AddLavalink();
        services.ConfigureLavalink(c =>
        {
            c.BaseAddress = new Uri(config["Lavalink:BaseAddress"] ?? "http://localhost:2333");
            c.Passphrase = config["Lavalink:Passphrase"]
                           ?? throw new InvalidOperationException("Lavalink:Passphrase is not configured.");
        });

        // services.AddVoiceExtension();
        services.ConfigureEventHandlers(b =>
        {
            b.AddEventHandlers<ReadyHandler>();
            b.AddEventHandlers<MessageHandler>();
            b.AddEventHandlers<ModalHandler>();
        });
        services.AddSingleton<IDisplayNameResolver, DisplayNameResolver>();
        services.AddSingleton<IMessagePayloadBuilder, MessagePayloadBuilder>();
        // services.AddSingleton<IVoiceService, DiscordVoiceService>();
        services.AddSingleton<IVoiceService, LavalinkVoiceService>();

        // Tools
        services.AddSingleton<IToolProvider, VoiceTools>();

        return services;
    }
}