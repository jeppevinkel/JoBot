// using JoBot.Core.Models;
//
// namespace JoBot.Core.Interfaces;
//
// public interface ISubsonicClient : IMusicClient
// {
//     Task<List<SongResult>> GetRandomSongsAsync(int count = 10);
//     string GetStreamUrl(string songId, int? maxBitRate = null);
//     Task<Stream> GetAudioStreamAsync(string songId);
//     Task<SubsonicShare> CreateShareAsync(
//         IEnumerable<string> songIds,
//         string? description = null,
//         DateTime? expires = null,
//         CancellationToken cancellationToken = default);
//     Task<List<SubsonicShare>> GetSharesAsync(CancellationToken cancellationToken = default);
//     Task DeleteShareAsync(string shareId, CancellationToken cancellationToken = default);
//     Task UpdateShareAsync(
//         string shareId,
//         string? description = null,
//         DateTime? expires = null,
//         CancellationToken cancellationToken = default);
// }