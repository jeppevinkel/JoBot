using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.HighPerformance.Buffers;
using DSharpPlus.Voice;
using DSharpPlus.Voice.Cryptors;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Voice.Native;

/// <summary>
/// Works out which user an incoming audio stream belongs to when Discord hasn't said.
/// </summary>
/// <remarks>
/// <para>
/// DSharpPlus.Voice learns which RTP SSRC belongs to which user from Discord's Speaking events, and drops audio from
/// SSRCs it doesn't know. Discord doesn't always send one for people who were already talking before the bot joined,
/// so their audio would be dropped for the whole call.
/// </para>
/// <para>
/// With DAVE, each user's audio can only be decrypted with that user's key. So for an unknown stream, one packet is
/// decrypted as each person in the channel who hasn't been identified yet and whose key we have (see
/// <see cref="GuardedE2EESession"/> for why that matters); if exactly one of them succeeds, that's
/// the speaker, and the library is told just as if Discord had sent the Speaking event. Streams from other bots
/// don't decrypt as anyone, so they're never mistaken for a person.
/// </para>
/// <para>
/// Created per connection from its service scope. The transport hands it every received packet, before the library
/// sees them.
/// </para>
/// </remarks>
public sealed class SpeakerIdentifier(GuardedE2EESession e2ee, ILogger<SpeakerIdentifier> logger)
{
    private const byte OpusPayloadType = 0x78;
    private const int RtpHeaderSize = 12;

    // How often to try identifying the same unknown stream; each attempt costs a decryption per candidate
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(500);

    private static readonly FieldInfo? SsrcsField =
        typeof(VoiceConnection).GetField("ssrcs", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? RegisterSpeakingMethod =
        typeof(VoiceConnection).GetMethod("RegisterSpeakingStatusAsync", BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(uint), typeof(ulong), typeof(bool)]);

    private readonly ConcurrentDictionary<uint, long> _lastAttempt = new();
    private volatile ICryptor? _cryptor;
    private VoiceConnection? _connection;
    private ConcurrentDictionary<uint, ulong>? _ssrcs;
    private Func<IReadOnlyCollection<ulong>>? _getPeopleInChannel;
    private int _attempts;
    private int _noCandidates;
    private int _noMatch;

    /// <summary>Called with each transport cryptor the connection creates.</summary>
    public void SetCryptor(ICryptor cryptor) => _cryptor = cryptor;

    /// <param name="getPeopleInChannel">Users other than the bot and other bots currently in the voice channel.</param>
    /// <returns>False if this version of the library can't be told about speakers.</returns>
    public bool Attach(VoiceConnection connection, Func<IReadOnlyCollection<ulong>> getPeopleInChannel)
    {
        _ssrcs = SsrcsField?.GetValue(connection) as ConcurrentDictionary<uint, ulong>;
        if (_ssrcs is null || RegisterSpeakingMethod is null || !GuardedE2EESession.CanReadRoster)
            return false;

        _getPeopleInChannel = getPeopleInChannel;
        _connection = connection;
        return true;
    }

    /// <summary>Inspects a received packet, before the library processes it.</summary>
    public void Inspect(ReadOnlySpan<byte> packet)
    {
        if (_connection is null || _ssrcs is null || _cryptor is not { } cryptor)
            return;

        if (packet.Length <= RtpHeaderSize || (packet[0] & 0b1100_0000) != 0b1000_0000 || (packet[1] & 0x7F) != OpusPayloadType)
            return;

        var ssrc = BinaryPrimitives.ReadUInt32BigEndian(packet[8..]);
        if (_ssrcs.ContainsKey(ssrc))
            return;

        var now = Stopwatch.GetTimestamp();
        if (_lastAttempt.TryGetValue(ssrc, out var last) && Stopwatch.GetElapsedTime(last, now) < RetryInterval)
            return;

        _lastAttempt[ssrc] = now;

        try
        {
            if (Identify(cryptor, packet) is { } userId)
                Register(ssrc, userId);
        }
        catch (Exception ex)
        {
            // Never let this break receiving for the library
            logger.LogDebug(ex, "Failed to identify audio stream {Ssrc}", ssrc);
        }
    }

    /// <summary>Returns and resets how often identification was attempted, and why it failed.</summary>
    public (int Attempts, int NoCandidates, int NoMatch) TakeStats() => (
        Interlocked.Exchange(ref _attempts, 0),
        Interlocked.Exchange(ref _noCandidates, 0),
        Interlocked.Exchange(ref _noMatch, 0));

    /// <summary>The streams the library currently knows, as SSRC → user ID.</summary>
    public IReadOnlyDictionary<uint, ulong> KnownStreams => _ssrcs ?? new ConcurrentDictionary<uint, ulong>();

    private ulong? Identify(ICryptor cryptor, ReadOnlySpan<byte> packet)
    {
        Interlocked.Increment(ref _attempts);

        var known = _ssrcs!.Values.ToHashSet();
        var candidates = _getPeopleInChannel!().Where(user => !known.Contains(user) && e2ee.HasKeyFor(user)).ToList();
        if (candidates.Count == 0)
        {
            Interlocked.Increment(ref _noCandidates);
            return null;
        }

        using var transportDecrypted = new ArrayPoolBufferWriter<byte>();
        if (!cryptor.Decrypt(packet, transportDecrypted, out _))
        {
            Interlocked.Increment(ref _noMatch);
            return null;
        }

        ReadOnlySpan<byte> frame = transportDecrypted.WrittenSpan;
        var buffer = ArrayPool<byte>.Shared.Rent(frame.Length);
        try
        {
            ulong? match = null;
            foreach (var candidate in candidates)
            {
                if (e2ee.DecryptFrame(candidate, frame, buffer) <= 0)
                    continue;

                // More than one match means the frame isn't end-to-end encrypted (e.g. a silence frame), so it
                // doesn't tell us anything
                if (match is not null)
                    return null;

                match = candidate;
            }

            if (match is null)
                Interlocked.Increment(ref _noMatch);

            return match;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Register(uint ssrc, ulong userId)
    {
        logger.LogDebug("Identified audio stream {Ssrc} as user {UserId}", ssrc, userId);
        _lastAttempt.TryRemove(ssrc, out _);

        // Completes synchronously with our receiver; observe it anyway so failures are logged
        var task = (Task)RegisterSpeakingMethod!.Invoke(_connection, [ssrc, userId, true])!;
        task.ContinueWith(t => logger.LogWarning(t.Exception, "Failed to register audio stream {Ssrc}", ssrc),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
