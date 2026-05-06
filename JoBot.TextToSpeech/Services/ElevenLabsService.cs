using ElevenLabs;
using ElevenLabs.TextToSpeech;
using ElevenLabs.Voices;
using JoBot.Core.Interfaces;
using JoBot.TextToSpeech.Configuration;
using Microsoft.Extensions.Options;

namespace JoBot.TextToSpeech.Services;

public class ElevenLabsService : ITtsService
{
    private readonly ITtsAudioStore _store;
    private readonly ElevenLabsClient _client;
    private readonly TextToSpeechOptions _textToSpeechOptions;
    private readonly ElevenLabsOptions _elevenLabsOptions;
    private Voice? _voice;

    public ElevenLabsService(ITtsAudioStore store, ElevenLabsClient client, IOptions<TextToSpeechOptions> textToSpeechOptions, IOptions<ElevenLabsOptions> elevenLabsOptions)
    {
        _store = store;
        _client = client;
        _textToSpeechOptions = textToSpeechOptions.Value;
        _elevenLabsOptions = elevenLabsOptions.Value;
    }

    public async Task<string> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or whitespace", nameof(text));

        var hostName = _textToSpeechOptions.HostName ?? "http://host.docker.internal";
        var port = _textToSpeechOptions.Port;

        Voice voice = await GetVoice();
        var request = new TextToSpeechRequest(voice, text, outputFormat: OutputFormat.MP3_44100_128);
        VoiceClip response = await _client.TextToSpeechEndpoint.TextToSpeechAsync(request, cancellationToken: cancellationToken);
        var audio = response.ClipData.ToArray();
        var id = _store.Add(audio, BuildTrackTitle(text));
        return $"{hostName}:{port}/tts/{id}";
    }

    public async Task<Voice> GetVoice()
    {
        var voiceId = _elevenLabsOptions.VoiceId;
        _voice ??= await _client.VoicesEndpoint.GetVoiceAsync(voiceId);

        return _voice;
    }
    
    private static string BuildTrackTitle(string spokenText, int maxLength = 80)
    {
        const string ellipsis = "…";

        if (spokenText.Length <= maxLength)
            return $"TTS: {spokenText}";

        return $"TTS: {spokenText[..(maxLength - ellipsis.Length)]}{ellipsis}";
    }
}