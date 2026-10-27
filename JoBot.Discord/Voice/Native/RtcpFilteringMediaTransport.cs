using System.Buffers;
using System.Net;
using DSharpPlus.Voice.Transport;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Voice.Native;

/// <summary>
/// Wraps the library's UDP transport to keep received RTCP packets away from it.
/// </summary>
/// <remarks>
/// <para>
/// DSharpPlus.Voice (nightly 02613) parses every received packet that looks like RTCP inside its receive loop, and
/// throws on anything it doesn't fully understand, such as trailing bytes (Discord encrypts RTCP in the
/// <c>*_rtpsize</c> modes, appending a tag and nonce), unknown packet types or unknown source description items.
/// The exception silently ends the receive loop, so no more audio is received for the rest of the connection.
/// </para>
/// <para>
/// The library only uses received RTCP to note when each user last sent a sender report, so dropping it is harmless.
/// Remove this once the library's receive loop survives packets it can't parse.
/// </para>
/// </remarks>
public sealed class RtcpFilteringMediaTransport(ILogger<RtcpFilteringMediaTransport> logger) : IMediaTransportService
{
    private const int MinRtcpSize = 4;
    private const byte FirstRtcpType = 200;
    private const byte LastRtcpType = 204;

    private readonly MediaTransportService _inner = new();
    private int _dropped;

    public Task SetTargetAsync(IPEndPoint endpoint) => _inner.SetTargetAsync(endpoint);

    public Task SendAsync(ReadOnlyMemory<byte> buffer) => _inner.SendAsync(buffer);

    public async Task ReceiveAsync(IBufferWriter<byte> bufferWriter)
    {
        var buffer = new ArrayBufferWriter<byte>();

        while (true)
        {
            buffer.ResetWrittenCount();
            await _inner.ReceiveAsync(buffer);
            ReadOnlySpan<byte> packet = buffer.WrittenSpan;

            if (LooksLikeRtcp(packet))
            {
                if (Interlocked.Increment(ref _dropped) == 1)
                    logger.LogDebug("Dropping received RTCP packets (first: {Length} bytes)", packet.Length);

                continue;
            }

            packet.CopyTo(bufferWriter.GetSpan(packet.Length));
            bufferWriter.Advance(packet.Length);
            return;
        }
    }

    public Task DisconnectAsync() => _inner.DisconnectAsync();

    public void Dispose() => _inner.Dispose();

    /// <summary>The same check the library uses to decide a packet is RTCP rather than audio or a keepalive.</summary>
    private static bool LooksLikeRtcp(ReadOnlySpan<byte> packet) =>
        packet.Length >= MinRtcpSize &&
        packet.Length % 4 == 0 &&
        (packet[0] & 0b1100_0000) == 0b1000_0000 &&
        packet[1] is >= FirstRtcpType and <= LastRtcpType;
}
