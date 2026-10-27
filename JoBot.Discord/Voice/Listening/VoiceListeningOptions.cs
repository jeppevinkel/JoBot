namespace JoBot.Discord.Voice.Listening;

public class VoiceListeningOptions
{
    public const string SectionName = "VoiceListening";

    /// <summary>
    /// Listen to voice channels the bot is in and answer when spoken to. Requires the native voice backend
    /// and a transcription server (see <c>Transcription:BaseUrl</c>).
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Names the bot answers to, in addition to its display name in the guild and its username.
    /// Add common mis-transcriptions of its name here too, e.g. <c>"joe bot"</c>.
    /// </summary>
    public string[] WakeWords { get; init; } = [];

    /// <summary>
    /// How long after the bot answered someone that they can carry on talking to it without saying its name.
    /// Set to 00:00:00 to always require the name.
    /// </summary>
    public TimeSpan FollowUpWindow { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long a speaker must be quiet before what they said is transcribed.</summary>
    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromMilliseconds(800);

    /// <summary>Shorter utterances (coughs, clicks, "mm") are ignored.</summary>
    public TimeSpan MinUtteranceLength { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Longer utterances are cut and transcribed in pieces.</summary>
    public TimeSpan MaxUtteranceLength { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Speak replies with text-to-speech in the voice channel.</summary>
    public bool SpeakReplies { get; init; } = true;

    /// <summary>Post replies in the voice channel's text chat.</summary>
    public bool PostReplies { get; init; } = true;

    /// <summary>Post what the bot heard in the voice channel's text chat when it's addressed, before it answers.</summary>
    public bool PostTranscripts { get; init; } = true;
}
