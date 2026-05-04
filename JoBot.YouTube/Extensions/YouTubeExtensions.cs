using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;
using JoBot.YouTube.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JoBot.YouTube.Extensions;

public static class YouTubeExtensions
{
    public static IServiceCollection AddYouTube(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<YouTubeClient>(_ => new YouTubeClient());
        services.AddSingleton<IToolProvider, MusicTools>();
        return services;
    }
}