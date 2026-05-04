using JoBot.Core.Helpers;
using JoBot.Core.Interfaces.Repositories;
using JoBot.Data.Context;
using JoBot.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JoBot.Data.Extensions;

public static class DataExtensions
{
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config.GetConnectionString("JoBot");

        ConfigurationValidator.Validate(
            ("ConnectionStrings:JoBot", connectionString)
        );

        services.AddDbContextFactory<JoBotDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddSingleton<IConversationRepository, ConversationRepository>();
        services.AddSingleton<IGuildSettingsRepository, GuildSettingsRepository>();

        return services;
    }

    public static async Task MigrateAsync(this IHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        var factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<JoBotDbContext>>();

        await using JoBotDbContext context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }
}