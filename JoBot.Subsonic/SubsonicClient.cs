using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JoBot.Core.Interfaces;
using JoBot.Core.Models;
using JoBot.Subsonic.Exceptions;
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

    private string BuildUrl(string endpoint, Dictionary<string, string>? extra = null)
    {
        var (token, salt) = GenerateAuth();

        var @params = new Dictionary<string, string>
        {
            ["u"] = _username,
            ["t"] = token,
            ["s"] = salt,
            ["v"] = ApiVersion,
            ["c"] = ClientName,
            ["f"] = "json"
        };

        if (extra != null)
            foreach (var kv in extra)
                @params[kv.Key] = kv.Value;

        var query = string.Join("&", @params.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")
        );

        return $"{_baseUrl}/rest/{endpoint}?{query}";
    }

    public async Task<SubsonicResponse> GetResponseAsync(
        string endpoint,
        Dictionary<string, string>? extra = null,
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

    // Get a direct stream URL (can be passed to a media player)
    public string GetStreamUrl(string songId, int? maxBitRate = null)
    {
        var extra = new Dictionary<string, string> {["id"] = songId};
        if (maxBitRate.HasValue) extra["maxBitRate"] = maxBitRate.ToString()!;
        return BuildUrl("stream", extra);
    }

    public Task<string> GetStreamUrlAsync(string songId)
    {
        return Task.FromResult(GetStreamUrl(songId));
    }

    // Get raw audio stream
    public async Task<Stream> GetAudioStreamAsync(string songId)
    {
        return await _http.GetStreamAsync(GetStreamUrl(songId));
    }

    public async Task<List<SubsonicSong>> SearchSongsAsync(
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

    public async Task<List<SongResult>> SearchSongsAsync(string query, int count = 10)
    {
        List<SubsonicSong> songs = await SearchSongsAsync(query, count, CancellationToken.None);
        return songs.Select(s => new SongResult(
            s.Id,
            s.Title,
            GetStreamUrl(s.Id),
            s.Artist ?? "Unknown",
            s.Album ?? "Unknown",
            s.Duration
        )).ToList();
    }

    public async Task<List<SubsonicSong>> GetRandomSongsAsync(
        int count = 10,
        CancellationToken ct = default)
    {
        SubsonicResponse response = await GetResponseAsync("getRandomSongs", new()
        {
            ["size"] = count.ToString()
        }, ct);

        return response.RandomSongs?.Song ?? [];
    }

    public async Task<List<SongResult>> GetRandomSongsAsync(int count = 10)
    {
        var songs = await GetRandomSongsAsync(count, CancellationToken.None);
        
        return songs.Select(s => new SongResult(
            s.Id,
            s.Title,
            GetStreamUrl(s.Id),
            s.Artist ?? "Unknown",
            s.Album ?? "Unknown",
            s.Duration
        )).ToList();
    }
}