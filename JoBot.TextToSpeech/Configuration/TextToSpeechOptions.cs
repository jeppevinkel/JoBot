namespace JoBot.TextToSpeech.Configuration;

public class TextToSpeechOptions
{
    public const string SectionName = "TextToSpeech";
    
    public string? HostName { get; set; }
    public int Port { get; set; } = 5756;
}