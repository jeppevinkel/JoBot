using DSharpPlus.Commands;
using DSharpPlus.Commands.ContextChecks;
using JoBot.Discord.Attributes;
using Microsoft.Extensions.Configuration;

namespace JoBot.Discord.Checks;

public class AllowedUsersCheck : IContextCheck<AllowedUsersAttribute>
{
    private readonly IConfiguration _configuration;

    public AllowedUsersCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ValueTask<string?> ExecuteCheckAsync(AllowedUsersAttribute attribute, CommandContext context)
    {
        var allowedUsers = _configuration
            .GetSection($"Discord:AllowedUsers:{attribute.GroupName}")
            .Get<ulong[]>() ?? [];

        return allowedUsers.Contains(context.User.Id)
            ? ValueTask.FromResult<string?>(null)
            : ValueTask.FromResult<string?>($"You are not allowed to use this command.");
    }
}