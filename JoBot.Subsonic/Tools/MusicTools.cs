using JoBot.Core.Attributes;
using JoBot.Core.Helpers;
using JoBot.Core.Interfaces;
using JoBot.Subsonic.Interfaces;
using JoBot.Subsonic.Models;
using Microsoft.Extensions.Configuration;

namespace JoBot.Subsonic.Tools;

public class MusicTools : IToolProvider
{
    private readonly ISubsonicClient _subsonicClient;
    private readonly IConfiguration _config;
    private readonly string _subsonicUsername;

    public MusicTools(ISubsonicClient subsonicClient, IConfiguration config)
    {
        _subsonicClient = subsonicClient;
        _config = config;

        _subsonicUsername = _config["Subsonic:Username"] ?? throw new InvalidOperationException("Subsonic username not set");
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

    [AiTool("Get all existing music share links")]
    public async Task<string> GetSharesAsync()
    {
        var shares = await _subsonicClient.GetSharesAsync();
        shares = shares.Where(s => s.Username.Equals(_subsonicUsername, StringComparison.CurrentCultureIgnoreCase)).ToList();
        return shares.Count == 0 ? ToolResult.Failure("No shares found") : ToolResult.Success(shares);
    }

    [AiTool("Permanently delete a music share link")]
    public async Task<string> DeleteShareAsync(
        [AiParameter("The ID of the share to delete")] string shareId)
    {
        var shares = await _subsonicClient.GetSharesAsync();
        shares = shares.Where(s => s.Username.Equals(_subsonicUsername, StringComparison.CurrentCultureIgnoreCase)).ToList();
        SubsonicShare? share = shares.FirstOrDefault(s => s.Id == shareId);
        if (share == null)
        {
            return ToolResult.Failure($"Share with ID {shareId} not found");
        }

        await _subsonicClient.DeleteShareAsync(shareId);
        return ToolResult.Success($"Share with ID {shareId} deleted");
    }

    [AiTool("Create a new share link for one or more songs. Use SearchSongs first to get song IDs.")]
    public async Task<string> CreateShareAsync(
        [AiParameter("The IDs of the songs to share")] string[] songIds,
        [AiParameter("Description for the share", Required = false)] string? description = null,
        [AiParameter("Number of days until the share expires", Required = false)] int? expiresInDays = null)
    {
        var expires = expiresInDays.HasValue
            ? DateTime.UtcNow.AddDays(expiresInDays.Value)
            : (DateTime?)null;

        SubsonicShare share = await _subsonicClient.CreateShareAsync(songIds, description, expires);
        return ToolResult.Success(share);
    }
}