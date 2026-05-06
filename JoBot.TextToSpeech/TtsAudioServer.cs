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

        if (id is not null && _store.TryGet(id, out var audio))
        {
            context.Response.ContentType = "audio/mpeg";
            context.Response.ContentLength64 = audio.Length;
            await context.Response.OutputStream.WriteAsync(audio);
            // _store.Remove(id); // clean up after serving
        }
        else
        {
            context.Response.StatusCode = 404;
        }

        context.Response.Close();
    }
}