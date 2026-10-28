namespace JoBot.Core.Interfaces;

public interface ITranscriptionService
{
    /// <summary>
    /// Whether the transcription server is currently reachable. Callers should skip work that only
    /// makes sense with a transcript (e.g. buffering audio) while this is false.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Transcribes mono 16-bit PCM audio.</summary>
    /// <param name="samples">Mono 16-bit PCM samples.</param>
    /// <param name="sampleRate">Sample rate of <paramref name="samples"/> in Hz.</param>
    /// <param name="hotwords">Words the speaker is likely to say, such as the bot's name, to bias recognition towards.</param>
    /// <returns>The transcript, or null if the audio couldn't be transcribed.</returns>
    Task<string?> TranscribeAsync(
        ReadOnlyMemory<short> samples,
        int sampleRate,
        IReadOnlyCollection<string>? hotwords = null,
        CancellationToken cancellationToken = default);
}
