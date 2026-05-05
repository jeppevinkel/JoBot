namespace JoBot.Core.Interfaces;

public interface ITtsService
{
    Task<string> GenerateAsync(string text, CancellationToken cancellationToken = default);
}