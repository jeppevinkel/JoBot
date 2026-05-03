using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Extensions;

public static class DiscordClientExtensions
{
    public static async Task<DiscordGuild?> TryGetGuildAsync(this DiscordClient client, ulong guildId, ILogger? logger = null)
    {
        try
        {
            DiscordGuild guild = await client.GetGuildAsync(guildId);
            return guild;
        }
        catch (NotFoundException)
        {
            return null;
        }
        catch (BadRequestException badRequestException)
        {
            logger?.LogWarning(badRequestException, "Bad request fetching guild {GuildId}", guildId);
            return null;
        }
        catch (ServerErrorException serverErrorException)
        {
            logger?.LogError(serverErrorException, "Server error fetching guild {GuildId}", guildId);
            return null;
        }
    }

    public static async Task<(DiscordGuild? guild, string? failureReason)> TryGetGuildWithReasonAsync(this DiscordClient client, ulong guildId, ILogger? logger = null)
    {
        try
        {
            DiscordGuild guild = await client.GetGuildAsync(guildId);
            return (guild, null);
        }
        catch (NotFoundException)
        {
            return (null, $"Guild {guildId} was not found or the bot is not a member.");
        }
        catch (BadRequestException badRequestException)
        {
            logger?.LogWarning(badRequestException, "Bad request fetching guild {GuildId}", guildId);
            return (null, "The request to fetch the guild was invalid.");
        }
        catch (ServerErrorException serverErrorException)
        {
            logger?.LogError(serverErrorException, "Server error fetching guild {GuildId}", guildId);
            return (null, "Discord returned a server error while fetching the guild.");
        }
    }
}