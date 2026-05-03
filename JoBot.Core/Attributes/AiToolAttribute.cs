namespace JoBot.Core.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class AiToolAttribute : Attribute
{
    public string Description { get; }
    public AiToolAttribute(string description) => Description = description;
}