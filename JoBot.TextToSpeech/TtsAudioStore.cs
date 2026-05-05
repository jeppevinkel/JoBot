using System.Collections.Concurrent;

namespace JoBot.TextToSpeech;

public class TtsAudioStore : ITtsAudioStore
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    public string Add(byte[] audio)
    {
        var id = Guid.NewGuid().ToString("N");
        _cache[id] = audio;
        return id;
    }

    public bool TryGet(string id, out byte[] audio)
        => _cache.TryGetValue(id, out audio!);

    public void Remove(string id)
        => _cache.TryRemove(id, out _);
}