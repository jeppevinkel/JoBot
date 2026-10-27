namespace JoBot.Transcription.Configuration;

public class TranscriptionOptions
{
    public const string SectionName = "Transcription";

    /// <summary>
    /// Base address of an OpenAI-compatible transcription server (e.g. speaches), such as
    /// <c>http://192.168.1.50:8000</c>. Transcription is disabled when this is empty.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Model ID sent with each request. It must be installed on the server.</summary>
    public string Model { get; set; } = "deepdml/faster-whisper-large-v3-turbo-ct2";

    /// <summary>ISO-639-1 language code (e.g. <c>da</c>, <c>en</c>). Leave empty to let the model detect it.</summary>
    public string? Language { get; set; }

    /// <summary>Sent as a bearer token, for servers that require an API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Path that returns a success status code while the server is up.</summary>
    public string HealthPath { get; set; } = "/health";

    /// <summary>How often to check whether the server is reachable.</summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a single transcription may take. The first request after the server starts may need to
    /// load the model, so keep this generous.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
