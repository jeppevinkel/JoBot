using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace JoBot.YouTube;

public class YouTubeClient : IMusicClient
{
    private readonly YoutubeClient _client;

    public YouTubeClient()
    {
        _client = new YoutubeClient();
    }

    public async Task<List<SongResult>> SearchSongsAsync(string query, int count = 10,
        CancellationToken cancellationToken = default)
    {
        var results = await _client.Search.GetVideosAsync(query, cancellationToken)
            .Take(count)
            .ToListAsync(cancellationToken);

        var tasks = results.Select(async result => new SongResult(
            result.Url,
            result.Title,
            await GetStreamUrlAsync(result.Id, cancellationToken),
            result.Author.ChannelTitle,
            "",
            (int)(result.Duration?.TotalSeconds ?? -1)
        ));
        return [.. await Task.WhenAll(tasks)];
    }

    public async Task<string> GetStreamUrlAsync(string songId, CancellationToken cancellationToken = default)
    {
        StreamManifest streamManifest = await _client.Videos.Streams.GetManifestAsync(songId, cancellationToken);
        IStreamInfo streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate()
                                 ?? throw new InvalidOperationException($"No audio streams found for video {songId}");
        return streamInfo.Url;
    }
}