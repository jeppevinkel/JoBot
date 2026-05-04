using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JoBot.Core.Models;
using JoBot.Subsonic.Exceptions;
using JoBot.Subsonic.Interfaces;
using JoBot.Subsonic.Models;

namespace JoBot.Subsonic;

public class SubsonicClient : ISubsonicClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _username;
    private readonly string _password;
    private const string ApiVersion = "1.16.1";
    private const string ClientName = "JoBot";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SubsonicClient(string baseUrl, string username, string password)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _username = username;
        _password = password;
        _http = new HttpClient();
    }

    private (string token, string salt) GenerateAuth()
    {
        var salt = Guid.NewGuid().ToString("N")[..10];
        var token = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(_password + salt))
        ).ToLower();
        return (token, salt);
    }

    private string BuildUrl(string endpoint, IEnumerable<KeyValuePair<string, string>>? extra = null)
    {
        var (token, salt) = GenerateAuth();

        var @params = new List<KeyValuePair<string, string>>
        {
            new("u", _username),
            new("t", token),
            new("s", salt),
            new("v", ApiVersion),
            new("c", ClientName),
            new("f", "json")
        };

        if (extra != null)
            @params.AddRange(extra);

        var query = string.Join("&", @params.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")
        );

        return $"{_baseUrl}/rest/{endpoint}?{query}";
    }

    private string BuildUrl(string endpoint, Dictionary<string, string>? extra)
        => BuildUrl(endpoint, extra?.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)));

    public async Task<SubsonicResponse> GetResponseAsync(
        string endpoint,
        IEnumerable<KeyValuePair<string, string>>? extra = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(endpoint, extra);
        var json = await _http.GetStringAsync(url, cancellationToken);

        var envelope = JsonSerializer.Deserialize<SubsonicEnvelope>(json, JsonOptions)
                       ?? throw new InvalidOperationException("Empty response from server");

        var response = envelope.Response
                       ?? throw new InvalidOperationException("Missing response body");

        if (response.Status != "ok")
            throw new SubsonicException(
                response.Error?.Code ?? 0,
                response.Error?.Message ?? "Unknown error"
            );

        return response;
    }

    public async Task<SubsonicResponse> GetResponseAsync(
        string endpoint,
        Dictionary<string, string> extra,
        CancellationToken cancellationToken = default)
    {
        return await GetResponseAsync(endpoint, extra?.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)),
            cancellationToken);
    }

    // Get a direct stream URL (can be passed to a media player)
    public string GetStreamUrl(string songId, int? maxBitRate = null)
    {
        var extra = new Dictionary<string, string> { ["id"] = songId };
        if (maxBitRate.HasValue) extra["maxBitRate"] = maxBitRate.ToString()!;
        return BuildUrl("stream", extra);
    }

    public Task<string> GetStreamUrlAsync(string songId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetStreamUrl(songId));
    }

    // Get raw audio stream
    public async Task<Stream> GetAudioStreamAsync(string songId, CancellationToken cancellationToken = default)
    {
        return await _http.GetStreamAsync(GetStreamUrl(songId), cancellationToken);
    }

    public async Task<List<SubsonicSong>> SearchAsync(
        string query,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        SubsonicResponse response = await GetResponseAsync("search3", new()
        {
            ["query"] = query,
            ["songCount"] = count.ToString(),
            ["albumCount"] = "0",
            ["artistCount"] = "0"
        }, cancellationToken);

        return response.SearchResult3?.Song ?? [];
    }

    public async Task<List<SongResult>> SearchSongsAsync(string query, int count = 10, CancellationToken cancellationToken = default)
    {
        List<SubsonicSong> songs = await SearchAsync(query, count, CancellationToken.None);
        return songs.Select(s => new SongResult(
            s.Id,
            s.Title,
            GetStreamUrl(s.Id),
            s.Artist ?? "Unknown",
            s.Album ?? "Unknown",
            s.Duration
        )).ToList();
    }

    public async Task<List<SubsonicSong>> GetRandomAsync(
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        SubsonicResponse response = await GetResponseAsync("getRandomSongs", new()
        {
            ["size"] = count.ToString()
        }, cancellationToken);

        return response.RandomSongs?.Song ?? [];
    }

    public async Task<List<SongResult>> GetRandomSongsAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var songs = await GetRandomAsync(count, cancellationToken);

        return songs.Select(s => new SongResult(
            s.Id,
            s.Title,
            GetStreamUrl(s.Id),
            s.Artist ?? "Unknown",
            s.Album ?? "Unknown",
            s.Duration
        )).ToList();
    }

    public async Task<SubsonicShare> CreateShareAsync(
        IEnumerable<string> songIds,
        string? description = null,
        DateTime? expires = null,
        CancellationToken cancellationToken = default)
    {
        var extra = songIds
            .Select(id => new KeyValuePair<string, string>("id", id))
            .ToList();

        // BuildUrl needs to handle multiple values for the same key
        // see note below
        if (description != null)
            extra.Add(new("description", description));

        if (expires.HasValue)
        {
            var ms = new DateTimeOffset(expires.Value).ToUnixTimeMilliseconds().ToString();
            extra.Add(new("expires", ms));
        }

        var response = await GetResponseAsync("createShare", extra, cancellationToken);

        return response.Shares?.Share?.FirstOrDefault()
               ?? throw new InvalidOperationException("No share returned");
    }

    public async Task<List<SubsonicShare>> GetSharesAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync("getShares", cancellationToken: cancellationToken);
        return response.Shares?.Share ?? [];
    }

    public async Task DeleteShareAsync(string shareId, CancellationToken cancellationToken = default)
    {
        await GetResponseAsync("deleteShare", new() { ["id"] = shareId }, cancellationToken);
    }

    public async Task UpdateShareAsync(
        string shareId,
        string? description = null,
        DateTime? expires = null,
        CancellationToken cancellationToken = default)
    {
        var extra = new List<KeyValuePair<string, string>> { new("id", shareId) };

        if (description != null)
            extra.Add(new("description", description));

        if (expires.HasValue)
        {
            var ms = new DateTimeOffset(expires.Value).ToUnixTimeMilliseconds().ToString();
            extra.Add(new("expires", ms));
        }

        await GetResponseAsync("updateShare", extra, cancellationToken);
    }
}