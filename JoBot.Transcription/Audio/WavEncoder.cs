using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace JoBot.Transcription.Audio;

public static class WavEncoder
{
    private const int HeaderSize = 44;

    /// <summary>Wraps mono 16-bit PCM samples in a WAV container.</summary>
    public static byte[] EncodeMono16(ReadOnlySpan<short> samples, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channels * bitsPerSample / 8;

        var dataSize = samples.Length * blockAlign;
        var wav = new byte[HeaderSize + dataSize];
        Span<byte> span = wav;

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], HeaderSize - 8 + dataSize);
        "WAVE"u8.CopyTo(span[8..]);

        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16); // fmt chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], bitsPerSample);

        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);

        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(samples).CopyTo(span[HeaderSize..]);
        }
        else
        {
            for (var i = 0; i < samples.Length; i++)
                BinaryPrimitives.WriteInt16LittleEndian(span[(HeaderSize + i * 2)..], samples[i]);
        }

        return wav;
    }
}
