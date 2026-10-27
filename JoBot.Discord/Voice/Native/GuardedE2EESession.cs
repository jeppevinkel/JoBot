using System.Reflection;
using CommunityToolkit.HighPerformance.Buffers;
using DSharpPlus.Voice.E2EE;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Voice.Native;

/// <summary>
/// Wraps the library's DAVE session so decrypting audio from a user it has no key for fails instead of crashing.
/// </summary>
/// <remarks>
/// <para>
/// The native DAVE library (libkoana 0.2.1) looks up the user's decryptor with <c>std::map::operator[]</c> and calls
/// it without checking, so decrypting a frame as a user who isn't in the MLS group dereferences a null pointer and
/// kills the whole process without a trace. That happens if a frame is attributed to someone whose key hasn't
/// arrived yet, e.g. right after they joined.
/// </para>
/// <para>
/// Decryptors exist exactly for the users in the group's roster, so the roster is read after every change to the
/// group and decryption is refused for anyone not on it.
/// </para>
/// </remarks>
public sealed class GuardedE2EESession : IE2EESession
{
    private static readonly FieldInfo? KoanaField =
        typeof(MlsSession).GetField("koana", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? GetUserRosterMethod =
        KoanaField?.FieldType.GetMethod("GetUserRoster", BindingFlags.Instance | BindingFlags.Public);

    private readonly MlsSession _inner = new();
    private readonly ILogger<GuardedE2EESession> _logger;
    private volatile IReadOnlySet<ulong> _usersWithKeys = new HashSet<ulong>();
    private ulong _botId;
    private int _disposed;
    private int _refused;
    private int _failed;
    private int _decrypted;

    public GuardedE2EESession(ILogger<GuardedE2EESession> logger)
    {
        _logger = logger;

        if (!CanReadRoster)
            _logger.LogWarning("Can't read the DAVE roster with this version of DSharpPlus.Voice; received audio will be ignored");
    }

    /// <summary>Whether the roster can be read; if not, all decryption is refused to be safe.</summary>
    public static bool CanReadRoster => GetUserRosterMethod is not null;

    /// <summary>Whether audio from this user can currently be decrypted.</summary>
    public bool HasKeyFor(ulong userId) => _usersWithKeys.Contains(userId);

    /// <summary>The users whose audio can currently be decrypted.</summary>
    public IReadOnlySet<ulong> UsersWithKeys => _usersWithKeys;

    public int DecryptFrame(ulong userId, ReadOnlySpan<byte> encryptedFrame, Span<byte> decryptedFrame)
    {
        if (!HasKeyFor(userId))
        {
            Interlocked.Increment(ref _refused);
            return 0;
        }

        var written = _inner.DecryptFrame(userId, encryptedFrame, decryptedFrame);
        Interlocked.Increment(ref written > 0 ? ref _decrypted : ref _failed);
        return written;
    }

    /// <summary>Returns and resets how many decryptions were refused (no key), failed and succeeded.</summary>
    public (int Refused, int Failed, int Decrypted) TakeStats() => (
        Interlocked.Exchange(ref _refused, 0),
        Interlocked.Exchange(ref _failed, 0),
        Interlocked.Exchange(ref _decrypted, 0));

    public void Initialize(ushort protocolVersion, ulong channelId, ulong userId, uint ssrc)
    {
        _botId = userId;
        _inner.Initialize(protocolVersion, channelId, userId, ssrc);
        RefreshRoster();
    }

    public void ReinitializeE2EESession(ushort protocolVersion)
    {
        _inner.ReinitializeE2EESession(protocolVersion);
        RefreshRoster();
    }

    public bool ProcessCommit(ReadOnlySpan<byte> payload)
    {
        var result = _inner.ProcessCommit(payload);
        RefreshRoster();
        return result;
    }

    public void ProcessWelcome(ReadOnlySpan<byte> payload, ReadOnlySpan<ulong> roster)
    {
        _inner.ProcessWelcome(payload, roster);
        RefreshRoster();
    }

    public byte[] ProcessProposals(ReadOnlySpan<byte> payload, ReadOnlySpan<ulong> roster) =>
        _inner.ProcessProposals(payload, roster);

    public void SetExternalSender(ReadOnlySpan<byte> payload) => _inner.SetExternalSender(payload);

    public bool TryEncryptFrame(ReadOnlySpan<byte> unencryptedFrame, ArrayPoolBufferWriter<byte> encryptedFrame) =>
        _inner.TryEncryptFrame(unencryptedFrame, encryptedFrame);

    public int GetMaxEncryptedLength(int unencryptedLength) => _inner.GetMaxEncryptedLength(unencryptedLength);

    public void WriteKeyPackage(ArrayPoolBufferWriter<byte> writer) => _inner.WriteKeyPackage(writer);

    /// <remarks>
    /// Only called from the voice gateway's event loop, right after the group changed on that same thread, so the
    /// native roster isn't being modified while it's read.
    /// </remarks>
    private void RefreshRoster()
    {
        if (GetUserRosterMethod is null)
            return;

        try
        {
            var roster = (Dictionary<ulong, byte[]>)GetUserRosterMethod.Invoke(KoanaField!.GetValue(_inner), null)!;

            // The bot has no decryptor for itself
            var users = roster.Keys.Where(id => id != _botId).ToHashSet();
            _usersWithKeys = users;
            _logger.LogInformation("DAVE keys available for {Count} users: {Users}", users.Count, string.Join(", ", users));
        }
        catch (Exception ex)
        {
            // Not knowing who has keys, refusing to decrypt is the only safe option
            _usersWithKeys = new HashSet<ulong>();
            _logger.LogWarning(ex, "Failed to read the DAVE roster");
        }
    }

    public void Dispose()
    {
        // Registered as both this type and IE2EESession, so the container disposes it twice; the native context
        // must only be freed once
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _inner.Dispose();
    }
}
