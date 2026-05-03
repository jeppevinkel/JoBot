namespace JoBot.Core.Helpers;

public static class ConfigurationValidator
{
    public static void Validate(params (string Key, string? Value)[] entries)
    {
        var missingKeys = entries
            .Where(e => string.IsNullOrWhiteSpace(e.Value))
            .Select(e => $"  - {e.Key}")
            .ToList();

        if (missingKeys.Count > 0)
            throw new InvalidOperationException(
                "The following configuration values are missing or empty:\n" +
                string.Join("\n", missingKeys));
    }
}