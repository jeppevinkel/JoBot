using System.ComponentModel;
using System.Text;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Entities;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;

namespace JoBot.Discord.Commands;

[Command("settings")]
public class SettingsCommands
{
    private readonly IGuildSettingsService _settingsService;

    public SettingsCommands(IGuildSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [Command("view")]
    [SlashCommandTypes(DiscordApplicationCommandType.SlashCommand)]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageGuild, DiscordPermission.ManageChannels, DiscordPermission.ManageMessages, DiscordPermission.ViewGuildInsights, DiscordPermission.ModerateMembers])]
    public async ValueTask ViewSettingsAsync(SlashCommandContext ctx)
    {
        GuildSettings settings = await _settingsService.GetSettingsAsync(ctx.Guild!.Id);

        DiscordEmbed embed = new DiscordEmbedBuilder()
            .WithTitle("Guild Settings")
            .WithColor(DiscordColor.Blurple)
            .AddField("Max History", settings.MaxHistoryMessages.ToString(), inline: true)
            .AddField("Temperature", settings.AiTemperature.ToString("F1"), inline: true)
            .AddField("Music Volume", $"{settings.MusicVolume * 100:F0}%", inline: true)
            .AddField("System Prompt", "See attached file.", inline: false)
            .Build();

        var promptBytes = Encoding.UTF8.GetBytes(settings.SystemPrompt);
        using var promptStream = new MemoryStream(promptBytes);

        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .AddEmbed(embed)
            .AddFile("system-prompt.txt", promptStream)
            .AsEphemeral());
    }

    [Command("set")]
    public class SetCommands
    {
        private readonly IGuildSettingsService _settingsService;

        public SetCommands(IGuildSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        [Command("prompt")]
        [SlashCommandTypes(DiscordApplicationCommandType.SlashCommand)]
        [RequireGuild]
        [RequirePermissions([], [DiscordPermission.ManageGuild, DiscordPermission.ManageChannels, DiscordPermission.ManageMessages])]
        public async ValueTask SetPromptAsync(SlashCommandContext ctx)
        {
            GuildSettings settings = await _settingsService.GetSettingsAsync(ctx.Guild!.Id);
            
            DiscordModalBuilder modal = new DiscordModalBuilder()
                .WithTitle("Set System Prompt")
                .WithCustomId("settings:set_prompt")
                .AddTextInput(new DiscordTextInputComponent(
                        "system_prompt",
                        "You are a helpful Discord bot...",
                        settings.SystemPrompt,
                        true,
                        DiscordTextInputStyle.Paragraph,
                        0,
                        4000)
                    , "System Prompt");

            await ctx.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.Modal,
                modal);
        }

        [Command("max-history")]
        [SlashCommandTypes(DiscordApplicationCommandType.SlashCommand)]
        [RequireGuild]
        [RequirePermissions([], [DiscordPermission.ManageGuild, DiscordPermission.ManageChannels, DiscordPermission.ManageMessages])]
        public async ValueTask SetMaxHistoryAsync(
            SlashCommandContext ctx,
            [Parameter("messages"), Description("Number of messages to keep in history (1-100)")]
            int messages)
        {
            if (messages is < 1 or > 100)
            {
                await ctx.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("Max history must be between 1 and 100.")
                        .AsEphemeral());
                return;
            }

            await _settingsService.UpdateSettingsAsync(ctx.Guild!.Id, new GuildSettingsUpdate
            {
                MaxHistoryMessages = messages
            });

            await ctx.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent($"Max history messages set to {messages}.")
                    .AsEphemeral());
        }

        [Command("temperature")]
        [SlashCommandTypes(DiscordApplicationCommandType.SlashCommand)]
        [RequireGuild]
        [RequirePermissions([], [DiscordPermission.ManageGuild, DiscordPermission.ManageChannels, DiscordPermission.ManageMessages])]
        public async ValueTask SetTemperatureAsync(
            SlashCommandContext ctx,
            [Parameter("temperature"), Description("AI temperature between 0.0 (precise) and 1.0 (creative)")]
            double temperature)
        {
            if (temperature is < 0.0 or > 1.0)
            {
                await ctx.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent("Temperature must be between 0.0 and 1.0.")
                        .AsEphemeral());
                return;
            }

            await _settingsService.UpdateSettingsAsync(ctx.Guild!.Id, new GuildSettingsUpdate
            {
                AiTemperature = (decimal)temperature
            });

            await ctx.Interaction.CreateResponseAsync(
                DiscordInteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .WithContent($"Temperature set to {temperature:F1}.")
                    .AsEphemeral());
        }

        [Command("volume")]
        [SlashCommandTypes(DiscordApplicationCommandType.SlashCommand)]
        [RequireGuild]
        public async ValueTask SetVolumeAsync(
            SlashCommandContext ctx,
            [Parameter("volume"), Description("Music volume (0-100)")]
            int volume)
        {
            if (volume is < 0 or > 100)
            {
                await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                    .WithContent("Volume must be between 0 and 100.")
                    .AsEphemeral());
                return;
            }

            await _settingsService.UpdateSettingsAsync(ctx.Guild!.Id, new GuildSettingsUpdate
            {
                MusicVolume = volume / 100f
            });

            await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
                .WithContent($"Music volume set to {volume}%.")
                .AsEphemeral());
        }
    }

    [Command("reset")]
    [Description("Reset a specific setting to its default value")]
    [SlashCommandTypes(DiscordApplicationCommandType.SlashCommand)]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public async ValueTask ResetFieldAsync(
        SlashCommandContext ctx,
        [Parameter("setting"), Description("The setting to reset to its default value")]
        SettingField field)
    {
        await _settingsService.ResetFieldAsync(ctx.Guild!.Id, field);

        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent($"{field} has been reset to its default value.")
            .AsEphemeral());
    }

    [Command("reset-all")]
    [SlashCommandTypes(DiscordApplicationCommandType.SlashCommand)]
    [RequireGuild]
    [RequirePermissions([], [DiscordPermission.ManageGuild])]
    public async ValueTask ResetAllAsync(SlashCommandContext ctx)
    {
        await _settingsService.ResetSettingsAsync(ctx.Guild!.Id);

        await ctx.RespondAsync(new DiscordInteractionResponseBuilder()
            .WithContent("All settings have been reset to defaults.")
            .AsEphemeral());
    }
}