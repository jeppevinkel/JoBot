namespace JoBot.Core.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public class AiParameterAttribute : Attribute
{
    public string Description { get; }
    public bool Required { get; init; } = true;
    public AiParameterAttribute(string description) => Description = description;
}