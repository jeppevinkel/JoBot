using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Handlers;

public class ModalHandler : IEventHandler<ModalSubmittedEventArgs>
{
    private readonly ILogger<ModalHandler> _logger;

    public ModalHandler(ILogger<ModalHandler> logger)
    {
        _logger = logger;
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

        if (prompt is TextInputModalSubmission textInput)
        {
            _logger.LogInformation("Setting system prompt to:\n{Prompt}", textInput.Value);
        }

        // await _settingsService.UpdateSettingsAsync(
        //     eventArgs.Guild!.Id,
        //     new GuildSettingsUpdate { SystemPrompt = prompt });

        await eventArgs.Interaction.CreateResponseAsync(
            DiscordInteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .WithContent("System prompt updated.")
                .AsEphemeral());
    }
}