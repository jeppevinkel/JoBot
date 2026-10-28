namespace JoBot.TextToSpeech.Configuration;

public class ElevenLabsOptions
{
    public const string SectionName = "ElevenLabs";

    public string VoiceId { get; set; } = null!;

    /// <summary>Lower values give a more expressive, varied delivery; higher values are more monotone. Range 0–1.</summary>
    public float Stability { get; set; } = 0.29f;

    /// <summary>How closely the output should match the original voice. Range 0–1.</summary>
    public float SimilarityBoost { get; set; } = 0.58f;

    /// <summary>Style exaggeration. Range 0–1.</summary>
    public float Style { get; set; } = 0f;

    public bool SpeakerBoost { get; set; } = true;

    public float Speed { get; set; } = 1f;
}
