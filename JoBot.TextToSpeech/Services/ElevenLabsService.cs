using ElevenLabs;
using ElevenLabs.TextToSpeech;
using ElevenLabs.Voices;
using JoBot.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace JoBot.TextToSpeech.Services;

public class ElevenLabsService : ITtsService
{
    private readonly ITtsAudioStore _store;
    private readonly TtsAudioServer _server;
    private readonly ElevenLabsClient _client;
    private readonly IConfiguration _config;
    private Voice? _voice;

    public ElevenLabsService(ITtsAudioStore store, TtsAudioServer server, ElevenLabsClient client, IConfiguration config)
    {
        _store = store;
        _server = server;
        _client = client;
        _config = config;
    }

    public async Task<string> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or whitespace", nameof(text));
        
        var hostName = _config["TextToSpeech:HostName"] ?? "http://host.docker.internal";

        Voice voice = await GetVoice();
        var request = new TextToSpeechRequest(voice, text, outputFormat: OutputFormat.MP3_44100_128);
        VoiceClip response = await _client.TextToSpeechEndpoint.TextToSpeechAsync(request, cancellationToken: cancellationToken);
        var audio = response.ClipData.ToArray();
        var id = _store.Add(audio);
        return $"{hostName}:{_server.Port}/tts/{id}";
    }

    public async Task<Voice> GetVoice()
    {
        var voiceId = _config["ElevenLabs:VoiceId"] ?? "21m00Tcm4TlvDq8ikWAM";
        _voice ??= await _client.VoicesEndpoint.GetVoiceAsync(voiceId);

        return _voice;
    }
}