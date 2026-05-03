using JoBot.Core.Attributes;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;

namespace JoBot.Subsonic.Tools;

public class MusicTools : IToolProvider
{
    private readonly ISubsonicClient _subsonicClient;
    
    public MusicTools(ISubsonicClient subsonicClient)
    {
        _subsonicClient = subsonicClient;
    }
    
    [AiTool("Search for songs by title, artist or album")]
    public async Task<string> SearchSongsAsync(
        [AiParameter("The search query")] string query,
        [AiParameter("Maximum number of results", Required = false)] int limit = 5)
    {
        var searchResult = await _subsonicClient.SearchSongsAsync(query, limit);
        return searchResult.Count == 0 ? ToolResult.Failure("No songs found") : ToolResult.Success(searchResult);
    }

    [AiTool("Get random songs")]
    public async Task<string> GetRandomSongsAsync([AiParameter("Maximum number of results", Required = false)] int limit = 1)
    {
        var songs = await _subsonicClient.GetRandomSongsAsync(limit);

        return songs.Count == 0 ? ToolResult.Failure("No songs found") : ToolResult.Success(songs);
    }
}


// private readonly ISubsonicClient _subsonicClient;
//
// public MusicTools(ISubsonicClient subsonicClient)
// {
//     _subsonicClient = subsonicClient;
// }
//     
// public IEnumerable<Tool> GetTools() =>
// [
//     Tool.GetOrCreateTool(this, nameof(SearchSongsAsync)),
//     Tool.GetOrCreateTool(this, nameof(GetRandomSongAsync)),
// ];
//
// [Function("Search for songs on the local Navidrome server")]
// public async Task<string> SearchSongsAsync(
//     [FunctionParameter("The search query to use", true)]
//     string query,
//     [FunctionParameter("The number of results to return", false)]
//     int count = 5)
// {
//     var searchResult = await _subsonicClient.SearchSongsAsync(query, count);
//
//     return searchResult.Count == 0 ? ToolResult.Failure("No songs found") : ToolResult.Success(searchResult);
// }
//
// [Function("Get a random song from the local Navidrome server")]
// public async Task<string> GetRandomSongAsync()
// {
//     var songs = await _subsonicClient.GetRandomSongsAsync(1);
//
//     return songs.Count == 0 ? ToolResult.Failure("No songs found") : ToolResult.Success(songs.First());
// }