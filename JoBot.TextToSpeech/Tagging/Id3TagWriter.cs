using System.Text;

namespace JoBot.TextToSpeech.Tagging;

/// <summary>
/// Prepends a minimal ID3v2.3 tag to raw MP3 bytes so metadata-aware
/// players (such as Lavalink/Lavaplayer) can surface a human-readable title.
/// </summary>
public static class Id3TagWriter
{
    private static readonly byte[] Utf16Bom = [0xFF, 0xFE];
    
    /// <summary>
    /// Returns a new byte array with an ID3v2.3 tag containing a TIT2 (title)
    /// frame prepended to <paramref name="mp3Data"/>.
    /// </summary>
    public static byte[] PrependTitle(byte[] mp3Data, string title)
    {
        var titleFrame = BuildTit2Frame(title);

        using var ms = new MemoryStream(10 + titleFrame.Length + mp3Data.Length);

        // ID3v2.3 global header — 10 bytes total
        ms.Write("ID3"u8);                  // magic
        ms.WriteByte(0x03);                       // version 2.3
        ms.WriteByte(0x00);                       // revision 0
        ms.WriteByte(0x00);                       // flags: none
        WriteSynchsafeInt(ms, titleFrame.Length); // size of tag body (synchsafe)

        ms.Write(titleFrame);
        ms.Write(mp3Data);

        return ms.ToArray();
    }
    
    /// Builds a raw TIT2 (title) frame using UTF-16 with BOM text encoding.
    private static byte[] BuildTit2Frame(string title)
    {
        var textBytes = Encoding.Unicode.GetBytes(title); // UTF-16 LE

        // Frame body = encoding byte (1) + BOM (2) + text bytes
        var frameBodySize = 1 + Utf16Bom.Length + textBytes.Length;

        using var ms = new MemoryStream(10 + frameBodySize);

        // Frame header — 10 bytes
        ms.Write("TIT2"u8);                   // frame ID
        WriteInt32BigEndian(ms, frameBodySize);     // body size — NOT synchsafe in v2.3
        ms.WriteByte(0x00);                         // status flags
        ms.WriteByte(0x00);                         // format flags

        // Frame body
        ms.WriteByte(0x01);                         // text encoding: UTF-16 with BOM
        ms.Write(Utf16Bom);                   // BOM: 0xFF 0xFE (little-endian)
        ms.Write(textBytes);

        return ms.ToArray();
    }
    
    /// <summary>
    /// Writes a 28-bit value as a 4-byte synchsafe integer.
    /// Each byte only uses its lower 7 bits; the MSB is always 0.
    /// This is required by the ID3v2 spec for tag-level sizes.
    /// </summary>
    private static void WriteSynchsafeInt(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 21) & 0x7F));
        stream.WriteByte((byte)((value >> 14) & 0x7F));
        stream.WriteByte((byte)((value >> 7)  & 0x7F));
        stream.WriteByte((byte)( value        & 0x7F));
    }

    private static void WriteInt32BigEndian(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8)  & 0xFF));
        stream.WriteByte((byte)( value        & 0xFF));
    }
}