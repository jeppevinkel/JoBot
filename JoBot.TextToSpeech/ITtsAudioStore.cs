namespace JoBot.TextToSpeech;

public interface ITtsAudioStore
{
    string Add(byte[] audio);
    bool TryGet(string id, out byte[] audio);
    void Remove(string id);
}