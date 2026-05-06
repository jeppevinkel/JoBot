using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Handlers;

public class ModalHandler : IEventHandler<ModalSubmittedEventArgs>
{
    private readonly ILogger<ModalHandler> _logger;
    private readonly IGuildSettingsService _guildSettingsService;

    public ModalHandler(ILogger<ModalHandler> logger, IGuildSettingsService settingsService)
    {
        _logger = logger;
        _guildSettingsService = settingsService;
    }

    public async Task HandleEventAsync(DiscordClient client, ModalSubmittedEventArgs eventArgs)
    {
        _logger.LogInformation("Modal submitted: {Modal}", eventArgs.Interaction.Data.CustomId);
        if (eventArgs.Interaction.Guild is null) return;

        switch (eventArgs.Interaction.Data.CustomId)
        {
            case "settings:set_prompt":
                await HandleSetPromptAsync(eventArgs);
                break;
        }
    }

    private async Task HandleSetPromptAsync(ModalSubmittedEventArgs eventArgs)
    {
        IModalSubmission prompt = eventArgs.Values["system_prompt"];

        if (prompt is not TextInputModalSubmission textInput)
        {
            throw new InvalidOperationException("Invalid modal submission");
        }

        if (eventArgs.Interaction.GuildId is not { } guildId)
        {
            throw new InvalidOperationException("Guild ID is null");
        }

        _logger.LogInformation("Setting system prompt to:\n{Prompt}", textInput.Value);

        await _guildSettingsService.UpdateSettingsAsync(
            guildId,
            new GuildSettingsUpdate { SystemPrompt = textInput.Value });

        await eventArgs.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("System prompt updated.")
                .AsEphemeral());
    }
}