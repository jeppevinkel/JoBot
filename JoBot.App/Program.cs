using JoBot.Ai.Extensions;
using JoBot.Data.Extensions;
using JoBot.Discord.Extensions;
using JoBot.Services.Extensions;
using JoBot.Subsonic.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

HostApplicationBuilder builder = Host.CreateApplicationBuilder();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/jobot-.log", rollingInterval: RollingInterval.Day)
    .Filter.ByExcluding(logEvent =>
        logEvent.Level == LogEventLevel.Warning &&
        logEvent.Exception is TaskCanceledException &&
        logEvent.Properties.TryGetValue("SourceContext", out var source) &&
        source.ToString().Contains("Lavalink4NET.Socket"))
    .CreateLogger();

builder.Logging.ClearProviders().AddSerilog();

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddAiServices(builder.Configuration);
builder.Services.AddSubsonic(builder.Configuration);
builder.Services.AddDiscordServices(builder.Configuration);
builder.Services.AddServices(builder.Configuration);

IHost app = builder.Build();

try
{
    await app.MigrateAsync();
    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}