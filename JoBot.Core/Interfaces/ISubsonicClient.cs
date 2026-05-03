using JoBot.Core.Models;

namespace JoBot.Core.Interfaces;

public interface ISubsonicClient : IMusicClient
{
    Task<List<SongResult>> GetRandomSongsAsync(int count = 10);
    string GetStreamUrl(string songId, int? maxBitRate = null);
    Task<Stream> GetAudioStreamAsync(string songId);
}