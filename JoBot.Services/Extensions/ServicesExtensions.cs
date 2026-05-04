using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JoBot.Services.Extensions;

public static class ServicesExtensions
{
    public static IServiceCollection AddServices(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddOptions<GuildSettings>()
            .Bind(config.GetSection("GuildDefaults"));

        services.AddSingleton<IGuildSettingsService, GuildSettingsService>();

        return services;
    }
}