using Anthropic.SDK;
using JoBot.Ai.Configuration;
using JoBot.Ai.Services;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JoBot.Ai.Extensions;

public static class AiServiceExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration config)
    {
        var apiKey = config["Anthropic:ApiKey"];

        ConfigurationValidator.Validate(
            ("Anthropic:ApiKey", apiKey)
        );

        services.AddOptions<AiOptions>()
            .Bind(config.GetSection(AiOptions.SectionName));

        services.AddSingleton<AnthropicClient>(_ => new AnthropicClient(apiKey));
        services.AddSingleton<IAiService, AiService>();
        return services;
    }
}