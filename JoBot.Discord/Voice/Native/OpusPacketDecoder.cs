using System.Runtime.InteropServices;

namespace JoBot.Discord.Voice.Native;

/// <summary>Decodes Opus packets to 48 kHz stereo 16-bit PCM with the libopus bundled with DSharpPlus.Voice.</summary>
/// <remarks>
/// Used instead of DSharpPlus.Voice's decoder, which (nightly 02613) passes <c>decode_fec = 1</c> to
/// <c>opus_decode</c>. That makes libopus decode the forward error correction data for the <em>previous</em> packet
/// instead of the packet itself, falling back to packet loss concealment, so received speech comes out as mush.
/// </remarks>
public sealed partial class OpusPacketDecoder : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    // 120 ms, the longest packet Opus allows
    private const int MaxSamplesPerChannel = SampleRate * 120 / 1000;

    private readonly short[] _pcm = new short[MaxSamplesPerChannel * Channels];
    private IntPtr _decoder;

    public OpusPacketDecoder()
    {
        _decoder = opus_decoder_create(SampleRate, Channels, out var error);
        if (error != 0 || _decoder == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create an Opus decoder (error {error})");
    }

    /// <summary>Decodes one packet.</summary>
    /// <returns>Interleaved stereo samples, valid until the next call.</returns>
    public ReadOnlySpan<short> Decode(ReadOnlySpan<byte> packet)
    {
        ObjectDisposedException.ThrowIf(_decoder == IntPtr.Zero, this);

        var samplesPerChannel = opus_decode(_decoder, packet, packet.Length, _pcm, MaxSamplesPerChannel, 0);
        if (samplesPerChannel < 0)
            throw new InvalidOperationException($"Failed to decode an Opus packet (error {samplesPerChannel})");

        return _pcm.AsSpan(0, samplesPerChannel * Channels);
    }

    public void Dispose()
    {
        if (_decoder == IntPtr.Zero)
            return;

        opus_decoder_destroy(_decoder);
        _decoder = IntPtr.Zero;
    }

    [LibraryImport("opus")]
    private static partial IntPtr opus_decoder_create(int sampleRate, int channels, out int error);

    [LibraryImport("opus")]
    private static partial int opus_decode(IntPtr decoder, ReadOnlySpan<byte> data, int length, Span<short> pcm, int frameSize, int decodeFec);

    [LibraryImport("opus")]
    private static partial void opus_decoder_destroy(IntPtr decoder);
}
