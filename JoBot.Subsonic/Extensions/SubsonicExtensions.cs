using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;
using JoBot.Subsonic.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JoBot.Subsonic.Extensions;

public static class SubsonicExtensions
{
    public static IServiceCollection AddSubsonic(this IServiceCollection services, IConfiguration config)
    {
        var baseUrl = config["Subsonic:BaseUrl"];
        var username = config["Subsonic:Username"];
        var password = config["Subsonic:Password"];
        
        ConfigurationValidator.Validate(
            ("Subsonic:BaseUrl", baseUrl),
            ("Subsonic:Username", username),
            ("Subsonic:Password", password)
        );

        services.AddSingleton<ISubsonicClient>(_ => new SubsonicClient(baseUrl!, username!, password!));
        services.AddSingleton<IToolProvider, MusicTools>();
        return services;
    }
}