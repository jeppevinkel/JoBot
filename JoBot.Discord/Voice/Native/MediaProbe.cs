using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Voice.Native;

/// <param name="Duration">The media length, or null if unknown (e.g. a live stream).</param>
public sealed record MediaInfo(TimeSpan? Duration, string? Title, string? Artist, string? Album);

public static class MediaProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly MediaInfo Unknown = new(null, null, null, null);

    /// <summary>
    /// Reads the duration and title/artist/album tags of the media at <paramref name="url"/>.
    /// Never throws for probe failures; unknown fields are null.
    /// </summary>
    public static async Task<MediaInfo> ProbeAsync(string ffprobePath, string url, ILogger logger, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration:format_tags=title,artist,album", "-of", "json", url })
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return Parse(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out probing media");
            return Unknown;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to probe media");
            return Unknown;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Never started or already exited
            }
        }
    }

    private static MediaInfo Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Unknown;

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("format", out JsonElement format))
            return Unknown;

        TimeSpan? duration = format.TryGetProperty("duration", out JsonElement durationElement)
                             && double.TryParse(durationElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                             && seconds > 0 && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

        format.TryGetProperty("tags", out JsonElement tags);

        return new MediaInfo(duration, GetTag(tags, "title"), GetTag(tags, "artist"), GetTag(tags, "album"));
    }

    private static string? GetTag(JsonElement tags, string name)
    {
        if (tags.ValueKind != JsonValueKind.Object)
            return null;

        // Tag casing varies between containers (e.g. "TITLE" in Ogg/FLAC)
        foreach (JsonProperty property in tags.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.GetString() is { Length: > 0 } value)
                return value;
        }

        return null;
    }
}
