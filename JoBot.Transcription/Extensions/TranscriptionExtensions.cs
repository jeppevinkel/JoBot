using JoBot.Core.Interfaces;
using JoBot.Transcription.Configuration;
using JoBot.Transcription.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JoBot.Transcription.Extensions;

public static class TranscriptionExtensions
{
    public static IServiceCollection AddTranscription(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<TranscriptionOptions>()
            .Bind(config.GetSection(TranscriptionOptions.SectionName));

        services.AddSingleton<OpenAiTranscriptionService>();
        services.AddSingleton<ITranscriptionService>(sp => sp.GetRequiredService<OpenAiTranscriptionService>());
        services.AddHostedService(sp => sp.GetRequiredService<OpenAiTranscriptionService>());

        return services;
    }
}
