namespace JoBot.Discord.Voice.Listening;

/// <summary>Something one user said in a voice channel, between two pauses.</summary>
/// <param name="Samples">Mono 16-bit PCM at <paramref name="SampleRate"/>.</param>
public sealed record Utterance(ulong GuildId, ulong ChannelId, ulong UserId, short[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}

/// <summary>Receives utterances captured from voice channels.</summary>
public interface IUtteranceSink
{
    /// <summary>
    /// Whether audio from this user is worth capturing at all. Called for every received packet, so it must be cheap.
    /// </summary>
    bool ShouldListenTo(ulong guildId, ulong userId);

    /// <summary>Queues an utterance for processing. Must not block.</summary>
    void Submit(Utterance utterance);
}
