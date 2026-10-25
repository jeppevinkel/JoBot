using System.Net;
using JoBot.TextToSpeech.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.TextToSpeech;

public class TtsAudioServer : IHostedService
{
    private readonly ITtsAudioStore _store;
    private readonly ILogger<TtsAudioServer> _logger;
    private readonly HttpListener _listener;
    private CancellationTokenSource? _cts;
    private readonly TextToSpeechOptions _textToSpeechOptions;

    public TtsAudioServer(ITtsAudioStore store, ILogger<TtsAudioServer> logger, IOptions<TextToSpeechOptions> textToSpeechOptions)
    {
        _store = store;
        _logger = logger;
        _listener = new HttpListener();
        _textToSpeechOptions = textToSpeechOptions.Value;
        _listener.Prefixes.Add($"http://*:{_textToSpeechOptions.Port}/tts/");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _logger.LogInformation("TTS audio server listening on port {Port}", _textToSpeechOptions.Port);

        Task.Run(() => ListenAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _listener.Stop();
        return Task.CompletedTask;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context); // fire and forget per request
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in TTS audio server");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var id = context.Request.Url?.Segments.LastOrDefault();

        try
        {
            if (id is not null && _store.TryGet(id, out var audio))
                await WriteAudioAsync(context, audio);
            else
                context.Response.StatusCode = 404;
        }
        catch (Exception ex)
        {
            // Lavalink closing the connection early (e.g. when a clip is stopped) is expected
            _logger.LogDebug(ex, "Error serving TTS audio {Id}", id);
        }
        finally
        {
            context.Response.Close();
        }
    }

    /// <summary>
    /// Writes the audio, honouring single-range <c>Range</c> requests. Lavaplayer resumes and seeks HTTP
    /// streams with <c>Range: bytes=N-</c> and accepts a plain 200 response, so ignoring the header makes
    /// it play the clip again from the start.
    /// </summary>
    private static async Task WriteAudioAsync(HttpListenerContext context, byte[] audio)
    {
        HttpListenerResponse response = context.Response;
        response.ContentType = "audio/mpeg";
        response.AddHeader("Accept-Ranges", "bytes");

        long start = 0;
        long end = audio.Length - 1;

        if (context.Request.Headers["Range"] is { } range)
        {
            if (!TryParseRange(range, audio.Length, out start, out end))
            {
                response.StatusCode = 416;
                response.AddHeader("Content-Range", $"bytes */{audio.Length}");
                return;
            }

            response.StatusCode = 206;
            response.AddHeader("Content-Range", $"bytes {start}-{end}/{audio.Length}");
        }

        var length = (int)(end - start + 1);
        response.ContentLength64 = length;

        if (context.Request.HttpMethod != "HEAD")
            await response.OutputStream.WriteAsync(audio.AsMemory((int)start, length));
    }

    private static bool TryParseRange(string header, long totalLength, out long start, out long end)
    {
        start = 0;
        end = totalLength - 1;

        const string prefix = "bytes=";
        if (totalLength == 0 || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var spec = header[prefix.Length..].Trim();
        if (spec.Contains(','))
            return false; // Multiple ranges aren't needed by Lavalink

        var dash = spec.IndexOf('-');
        if (dash < 0)
            return false;

        var startText = spec[..dash].Trim();
        var endText = spec[(dash + 1)..].Trim();

        if (startText.Length == 0)
        {
            // Suffix range: the last N bytes
            if (!long.TryParse(endText, out var suffixLength) || suffixLength <= 0)
                return false;
            start = Math.Max(0, totalLength - suffixLength);
            return true;
        }

        if (!long.TryParse(startText, out start) || start < 0 || start >= totalLength)
            return false;

        if (endText.Length > 0)
        {
            if (!long.TryParse(endText, out end) || end < start)
                return false;
            end = Math.Min(end, totalLength - 1);
        }

        return true;
    }
}