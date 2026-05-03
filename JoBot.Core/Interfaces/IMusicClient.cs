using JoBot.Core.Models;

namespace JoBot.Core.Interfaces;

public interface IMusicClient
{
    Task<List<SongResult>> SearchSongsAsync(string query, int count = 10);
    Task<string> GetStreamUrlAsync(string songId);
}