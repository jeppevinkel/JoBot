using System.Buffers.Binary;
using CommunityToolkit.HighPerformance.Buffers;
using DSharpPlus.Voice.Cryptors;
using DSharpPlus.Voice.Protocol.RTP;

namespace JoBot.Discord.Voice.Native;

/// <summary>
/// Wraps the library's cryptors to fix receiving audio.
/// </summary>
/// <remarks>
/// <para>
/// With Discord's <c>*_rtpsize</c> encryption modes, the body of an RTP header extension is encrypted together with
/// the audio, so it comes out at the start of the decrypted payload. DSharpPlus.Voice (nightly 02613) doesn't remove
/// it before handing the payload to DAVE decryption, so every received frame fails to decrypt. This removes it.
/// </para>
/// <para>
/// Remove this once the library strips the extension itself, otherwise audio would be cut twice.
/// </para>
/// </remarks>
public sealed class ExtensionStrippingCryptorFactory(ICryptorFactory inner) : ICryptorFactory
{
    public IReadOnlyList<string> SupportedEncryptionModes => inner.SupportedEncryptionModes;

    public string SelectPreferredEncryptionMode(params IEnumerable<string> supportedEncryptionModes) =>
        inner.SelectPreferredEncryptionMode(supportedEncryptionModes);

    public ICryptor CreateCryptor(string selectedEncryptionMode, byte[] key) =>
        new ExtensionStrippingCryptor(inner.CreateCryptor(selectedEncryptionMode, key));

    private sealed class ExtensionStrippingCryptor(ICryptor inner) : ICryptor
    {
        private const int RtpHeaderSize = 12;
        private const byte ExtensionFlag = 0x10;
        private const byte CsrcCountMask = 0x0F;

        public string EncryptionMode => inner.EncryptionMode;

        public void Encrypt(ReadOnlySpan<byte> frame, ArrayPoolBufferWriter<byte> encrypted) =>
            inner.Encrypt(frame, encrypted);

        public int GetMaxEncryptedLength(int unencrypted) => inner.GetMaxEncryptedLength(unencrypted);

        public bool Decrypt(ReadOnlySpan<byte> encryptedFrame, ArrayPoolBufferWriter<byte> decrypted, out RTPFrameInfo frameInfo)
        {
            var extensionLength = GetExtensionBodyLength(encryptedFrame);
            if (extensionLength == 0)
                return inner.Decrypt(encryptedFrame, decrypted, out frameInfo);

            using var buffer = new ArrayPoolBufferWriter<byte>();
            if (!inner.Decrypt(encryptedFrame, buffer, out frameInfo) || buffer.WrittenCount < extensionLength)
                return false;

            ReadOnlySpan<byte> payload = buffer.WrittenSpan[extensionLength..];
            payload.CopyTo(decrypted.GetSpan(payload.Length));
            decrypted.Advance(payload.Length);
            return true;
        }

        /// <returns>Length in bytes of the (encrypted) extension body, or 0 if the packet has no extension.</returns>
        private static int GetExtensionBodyLength(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < RtpHeaderSize || (frame[0] & ExtensionFlag) == 0)
                return 0;

            // The extension header itself (profile and length in 32-bit words) is not encrypted
            var extensionHeader = RtpHeaderSize + (frame[0] & CsrcCountMask) * 4;
            if (frame.Length < extensionHeader + 4)
                return 0;

            return BinaryPrimitives.ReadUInt16BigEndian(frame[(extensionHeader + 2)..]) * 4;
        }
    }
}
