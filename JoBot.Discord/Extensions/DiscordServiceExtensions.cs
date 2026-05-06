using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.SlashCommands.InteractionNamingPolicies;
using DSharpPlus.Commands.Processors.TextCommands;
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
        var token = config["Discord:Token"];
        var debugGuildId = ulong.Parse(config["Discord:DebugGuildId"] ?? "0");
        var lavalinkBaseAddress = config["Lavalink:BaseAddress"] ?? "http://localhost:2333";
        var lavalinkPassphrase = config["Lavalink:Passphrase"];

        Core.Helpers.ConfigurationValidator.Validate(
            ("Discord:Token", token),
            ("Lavalink:Passphrase", lavalinkPassphrase)
        );

        services.AddHostedService<DiscordBotService>();
        services.AddDiscordClient(token!, DiscordIntents.AllUnprivileged | DiscordIntents.MessageContents);

        SlashCommandProcessor slashCommandProcessor = new(new SlashCommandConfiguration()
        {
            NamingPolicy = new KebabCaseNamingPolicy(),
        });

        services.AddCommandsExtension((provider, extension) =>
        {
            extension.AddProcessor(slashCommandProcessor);

            extension.AddCommands<SettingsCommands>();
        }, new CommandsConfiguration
        {
            DebugGuildId = debugGuildId,
            RegisterDefaultCommandProcessors = false
        });

        services.AddLavalink();
        services.ConfigureLavalink(c =>
        {
            c.BaseAddress = new Uri(lavalinkBaseAddress);
            c.Passphrase = lavalinkPassphrase!;
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