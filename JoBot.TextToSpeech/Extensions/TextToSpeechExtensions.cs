using ElevenLabs;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;
using JoBot.TextToSpeech.Configuration;
using JoBot.TextToSpeech.Services;
using JoBot.TextToSpeech.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JoBot.TextToSpeech.Extensions;

public static class TextToSpeechExtensions
{
    public static IServiceCollection AddTextToSpeech(this IServiceCollection services, IConfiguration config)
    {
        var apiKey = config["ElevenLabs:ApiKey"];

        ConfigurationValidator.Validate(
            ("ElevenLabs:ApiKey", apiKey)
        );
        
        
        services.AddOptions<ElevenLabsOptions>()
            .Bind(config.GetSection(ElevenLabsOptions.SectionName));

        services.AddSingleton<ElevenLabsClient>(_ => new ElevenLabsClient(apiKey));
        services.AddSingleton<ITtsAudioStore, TtsAudioStore>();
        services.AddSingleton<TtsAudioServer>();
        services.AddHostedService(p => p.GetRequiredService<TtsAudioServer>());
        services.AddSingleton<ITtsService, ElevenLabsService>();
        services.AddSingleton<IToolProvider, TextToSpeechTools>();

        return services;
    }
}