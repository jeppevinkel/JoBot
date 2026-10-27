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
using JoBot.Discord.Voice;
using JoBot.Discord.Voice.Listening;
using JoBot.Discord.Voice.Native;
using DSharpPlus.Voice;
using DSharpPlus.Voice.Cryptors;
using DSharpPlus.Voice.E2EE;
using DSharpPlus.Voice.Transport;
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
        var voiceOptions = config.GetSection(VoiceBackendOptions.SectionName).Get<VoiceBackendOptions>() ?? new VoiceBackendOptions();
        var listeningOptions = config.GetSection(VoiceListeningOptions.SectionName).Get<VoiceListeningOptions>() ?? new VoiceListeningOptions();

        Core.Helpers.ConfigurationValidator.Validate(("Discord:Token", token));
        if (voiceOptions.Backend is VoiceBackend.Lavalink)
            Core.Helpers.ConfigurationValidator.Validate(("Lavalink:Passphrase", lavalinkPassphrase));
        if (listeningOptions.Enabled && voiceOptions.Backend is not VoiceBackend.Native)
            throw new InvalidOperationException(
                $"{VoiceListeningOptions.SectionName}:Enabled requires {VoiceBackendOptions.SectionName}:Backend to be {VoiceBackend.Native}");

        services.AddHostedService<DiscordBotService>();
        services.AddHostedService<VoiceInactivityService>();
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

        services.AddOptions<VoiceBackendOptions>()
            .Bind(config.GetSection(VoiceBackendOptions.SectionName));
        services.AddOptions<VoiceListeningOptions>()
            .Bind(config.GetSection(VoiceListeningOptions.SectionName));

        if (voiceOptions.Backend is VoiceBackend.Native)
        {
            services.AddVoiceExtension();
            services.AddSingleton<IVoiceService, NativeVoiceService>();

            if (listeningOptions.Enabled)
            {
                // Resolved from each voice connection's service scope
                services.AddScoped<ListeningAudioReceiver>();
                // Work around bugs in the library's receive path; see these classes
                services.AddScoped<GuardedE2EESession>();
                services.AddScoped<IE2EESession>(sp => sp.GetRequiredService<GuardedE2EESession>());
                services.AddScoped<SpeakerIdentifier>();
                services.AddScoped<ICryptorFactory>(sp => new ExtensionStrippingCryptorFactory(
                    ActivatorUtilities.CreateInstance<DefaultCryptorFactory>(sp),
                    sp.GetRequiredService<SpeakerIdentifier>().SetCryptor));
                services.AddScoped<IMediaTransportService, RtcpFilteringMediaTransport>();
                services.AddSingleton<VoiceListeningService>();
                services.AddSingleton<IUtteranceSink>(sp => sp.GetRequiredService<VoiceListeningService>());
                services.AddHostedService(sp => sp.GetRequiredService<VoiceListeningService>());
            }
        }
        else
        {
            services.AddLavalink();
            services.ConfigureLavalink(c =>
            {
                c.BaseAddress = new Uri(lavalinkBaseAddress);
                c.Passphrase = lavalinkPassphrase!;
            });
            services.AddSingleton<IVoiceService, LavalinkVoiceService>();
        }

        services.ConfigureEventHandlers(b =>
        {
            b.AddEventHandlers<ReadyHandler>();
            b.AddEventHandlers<MessageHandler>();
            b.AddEventHandlers<ModalHandler>();
        });
        services.AddSingleton<IDisplayNameResolver, DisplayNameResolver>();
        services.AddSingleton<IMessagePayloadBuilder, MessagePayloadBuilder>();

        // Tools
        services.AddSingleton<IToolProvider, VoiceTools>();

        return services;
    }
}