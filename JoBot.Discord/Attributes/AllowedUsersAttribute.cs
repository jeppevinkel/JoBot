using DSharpPlus.Commands.ContextChecks;

namespace JoBot.Discord.Attributes;

public class AllowedUsersAttribute : ContextCheckAttribute
{
    public string GroupName { get; init; }

    public AllowedUsersAttribute(string groupName) => GroupName = groupName;
}