using System.Net.Http.Headers;
using System.Text.Json;
using JoBot.Core.Interfaces;
using JoBot.Transcription.Audio;
using JoBot.Transcription.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JoBot.Transcription.Services;

/// <summary>
/// Transcribes audio with a server implementing OpenAI's <c>/v1/audio/transcriptions</c> endpoint, such as speaches.
/// </summary>
/// <remarks>
/// The server is expected to come and go (e.g. it runs on a desktop PC), so it's polled in the background and
/// <see cref="IsAvailable"/> reflects the last known state. A failed request also marks it unavailable straight away,
/// rather than waiting for the next health check.
/// </remarks>
public class OpenAiTranscriptionService : BackgroundService, ITranscriptionService
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<OpenAiTranscriptionService> _logger;
    private readonly TranscriptionOptions _options;
    private readonly HttpClient? _http;
    private volatile bool _isAvailable;

    public OpenAiTranscriptionService(ILogger<OpenAiTranscriptionService> logger, IOptions<TranscriptionOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            return;

        // Timeouts are applied per request, since health checks and transcriptions need different ones
        _http = new HttpClient
        {
            BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = Timeout.InfiniteTimeSpan
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
    }

    public bool IsAvailable => _isAvailable;

    public async Task<string?> TranscribeAsync(
        ReadOnlyMemory<short> samples,
        int sampleRate,
        IReadOnlyCollection<string>? hotwords = null,
        CancellationToken cancellationToken = default)
    {
        if (_http is null || !_isAvailable)
            return null;

        using var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(WavEncoder.EncodeMono16(samples.Span, sampleRate));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "audio.wav");
        form.Add(new StringContent(_options.Model), "model");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0"), "temperature");

        if (!string.IsNullOrWhiteSpace(_options.Language))
            form.Add(new StringContent(_options.Language), "language");

        // Not part of OpenAI's API; faster-whisper based servers use it to bias recognition towards these words,
        // others ignore it
        if (hotwords is { Count: > 0 })
            form.Add(new StringContent(string.Join(' ', hotwords)), "hotwords");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);

        try
        {
            using HttpResponseMessage response = await _http.PostAsync("v1/audio/transcriptions", form, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                // The server is up but rejected the request, e.g. because the model isn't installed
                var error = await response.Content.ReadAsStringAsync(timeout.Token);
                _logger.LogError("Transcription failed with {StatusCode}: {Body}", (int)response.StatusCode, error);
                return null;
            }

            await using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using JsonDocument result = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            return result.RootElement.TryGetProperty("text", out JsonElement text) ? text.GetString()?.Trim() : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetAvailable(false, $"no response within {_options.RequestTimeout}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            SetAvailable(false, ex.Message);
            return null;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_http is null)
        {
            _logger.LogInformation("Transcription is disabled because {Setting} is not set", $"{TranscriptionOptions.SectionName}:BaseUrl");
            return;
        }

        using var timer = new PeriodicTimer(_options.HealthCheckInterval);
        do
        {
            await CheckHealthAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckHealthAsync(CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(HealthCheckTimeout);

        try
        {
            using HttpResponseMessage response = await _http!.GetAsync(_options.HealthPath.TrimStart('/'), timeout.Token);
            if (response.IsSuccessStatusCode)
                SetAvailable(true, null);
            else
                SetAvailable(false, $"health check returned {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            SetAvailable(false, "health check timed out");
        }
        catch (HttpRequestException ex)
        {
            SetAvailable(false, ex.Message);
        }
    }

    private void SetAvailable(bool available, string? reason)
    {
        if (_isAvailable == available)
            return;

        _isAvailable = available;

        if (available)
            _logger.LogInformation("Transcription server at {BaseUrl} is available", _http!.BaseAddress);
        else
            _logger.LogWarning("Transcription server at {BaseUrl} is unavailable: {Reason}", _http!.BaseAddress, reason);
    }

    public override void Dispose()
    {
        _http?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
