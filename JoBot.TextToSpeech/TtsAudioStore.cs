using System.Collections.Concurrent;
using JoBot.TextToSpeech.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JoBot.TextToSpeech;

public class TtsAudioStore : ITtsAudioStore, IHostedService
{
    // How often the cleanup loop runs — well within the minimum 15-min active TTL
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(2);
    
    private readonly ConcurrentDictionary<string, TtsAudioEntry> _cache = new();
    private readonly ILogger<TtsAudioStore> _logger;
    
    private CancellationTokenSource? _cts;
    private Task? _cleanupTask;
    
    public TtsAudioStore(ILogger<TtsAudioStore> logger)
    {
        _logger = logger;
    }

    public string Add(byte[] audio)
    {
        var id = Guid.NewGuid().ToString("N");
        _cache[id] = new TtsAudioEntry(audio);
        _logger.LogDebug("Added TTS audio entry {Id}", id);
        return id;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Touching the entry on each access resets its expiry to the active TTL,
    /// so entries in active use won't be evicted.
    /// </remarks>
    public bool TryGet(string id, out byte[] audio)
    {
        if (_cache.TryGetValue(id, out var entry) && !entry.IsExpired)
        {
            entry.Touch();
            audio = entry.Audio;
            return true;
        }

        audio = null!;
        return false;
    }

    public void Remove(string id)
    {
        if (_cache.TryRemove(id, out _))
            _logger.LogDebug("Manually removed TTS audio entry {Id}", id);
    }
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cleanupTask = Task.Run(() => CleanupLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();

        if (_cleanupTask is not null)
        {
            try
            {
                await _cleanupTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
        }
    }
    
    private async Task CleanupLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, cancellationToken);
                EvictExpiredEntries();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in TTS audio store cleanup loop");
            }
        }

        // One final sweep on shutdown
        EvictExpiredEntries();
    }
    
    private void EvictExpiredEntries()
    {
        // Snapshot the keys first to avoid enumerating while mutating
        foreach ((var key, TtsAudioEntry entry) in _cache)
        {
            if (!entry.IsExpired)
                continue;

            if (_cache.TryRemove(key, out _))
                _logger.LogDebug("Evicted expired TTS audio entry {Id}", key);
        }
    }
}