using JoBot.Core.Attributes;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;

namespace JoBot.YouTube.Tools;

public class MusicTools : IToolProvider
{
    private readonly YouTubeClient _youTubeClient;

    public MusicTools(YouTubeClient youTubeClient)
    {
        _youTubeClient = youTubeClient;
    }

    [AiTool("Search for songs by title, artist or album")]
    public async Task<string> SearchYouTubeSongsAsync(
        [AiParameter("The search query")] string query,
        [AiParameter("Maximum number of results", Required = false)] int limit = 5)
    {
        var searchResult = await _youTubeClient.SearchSongsAsync(query, limit);
        return searchResult.Count == 0 ? ToolResult.Failure("No songs found") : ToolResult.Success(searchResult);
    }
}