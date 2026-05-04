using Anthropic.SDK.Constants;

namespace JoBot.Ai.Configuration;

public class AiOptions
{
    public const string SectionName = "Ai";

    public int MaxToolIterations { get; init; } = 50;
    public int MaxTokens { get; init; } = 4096;
    public string Model { get; init; } = AnthropicModels.Claude46Sonnet;
}