using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace JoBot.Discord.Voice.Native;

/// <summary>
/// Decodes a URL to 48 kHz stereo s16le PCM with ffmpeg, delivered as fixed-size 20 ms frames.
/// </summary>
/// <remarks>
/// Decoding runs on a background task into a small bounded buffer, so a stalled network stream never
/// blocks the mixer; <see cref="TryReadFrame"/> simply reports that no frame is ready yet.
/// </remarks>
public sealed class FfmpegAudioSource : IAsyncDisposable
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BytesPerSample = 2 * Channels;
    public const int SamplesPerFrame = SampleRate / 50; // 20 ms
    public const int FrameSize = SamplesPerFrame * BytesPerSample;

    // ~1 second of decoded audio buffered ahead of the mixer
    private const int BufferedFrames = 50;

    private readonly Process _process;
    private readonly Channel<byte[]> _frames;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;
    private readonly Task<string> _stderrTask;
    private readonly ILogger _logger;
    private readonly string _url;
    private readonly string _description;
    private readonly Action _onFrameAvailable;

    private FfmpegAudioSource(Process process, string url, ILogger logger, Action onFrameAvailable)
    {
        _process = process;
        _url = url;
        _description = DescribeUrl(url);
        _logger = logger;
        _onFrameAvailable = onFrameAvailable;
        _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(BufferedFrames)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        _pumpTask = Task.Run(PumpAsync);
    }

    /// <summary>True once ffmpeg has finished and every decoded frame has been read.</summary>
    public bool IsCompleted => _frames.Reader.Completion.IsCompleted;

    /// <summary>Number of frames delivered so far.</summary>
    public long FramesRead { get; private set; }

    /// <param name="startPosition">Position to start decoding from.</param>
    /// <param name="onFrameAvailable">Invoked from the decoding task whenever a new frame is buffered.</param>
    public static FfmpegAudioSource Start(
        string ffmpegPath,
        string url,
        TimeSpan startPosition,
        ILogger logger,
        Action onFrameAvailable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList avoids any quoting/injection issues with URLs
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin" })
            startInfo.ArgumentList.Add(arg);

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Recover from dropped connections mid-stream instead of ending the track early
            foreach (var arg in new[] { "-reconnect", "1", "-reconnect_streamed", "1", "-reconnect_delay_max", "5" })
                startInfo.ArgumentList.Add(arg);
        }

        if (startPosition > TimeSpan.Zero)
        {
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(startPosition.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var arg in new[] { "-i", url, "-vn", "-ac", "2", "-ar", "48000", "-f", "s16le", "pipe:1" })
            startInfo.ArgumentList.Add(arg);

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("ffmpeg failed to start.");

        return new FfmpegAudioSource(process, url, logger, onFrameAvailable);
    }

    /// <summary>Copies the next 20 ms frame into <paramref name="destination"/> if one is ready.</summary>
    public bool TryReadFrame(Span<byte> destination)
    {
        if (!_frames.Reader.TryRead(out var frame))
            return false;

        frame.CopyTo(destination);
        FramesRead++;
        return true;
    }

    private async Task PumpAsync()
    {
        Stream output = _process.StandardOutput.BaseStream;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var frame = new byte[FrameSize];
                var filled = 0;

                while (filled < FrameSize)
                {
                    var read = await output.ReadAsync(frame.AsMemory(filled), _cts.Token);
                    if (read == 0)
                        break;
                    filled += read;
                }

                if (filled == 0)
                    break;

                // A trailing partial frame is padded with silence
                await _frames.Writer.WriteAsync(frame, _cts.Token);
                _onFrameAvailable();

                if (filled < FrameSize)
                    break;
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading decoded audio for {Source}", _description);
        }
        finally
        {
            _frames.Writer.TryComplete();
            // Wake the mixer so it notices the end of the stream promptly
            _onFrameAvailable();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited
        }

        try
        {
            await _pumpTask.WaitAsync(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            var stderr = await _stderrTask.WaitAsync(TimeSpan.FromSeconds(1));
            // Killing ffmpeg mid-stream is expected; only report errors from streams that ran to completion
            if (FramesRead == 0 && !string.IsNullOrWhiteSpace(stderr))
                _logger.LogWarning("ffmpeg could not decode {Source}: {Error}", _description, Redact(stderr.Trim()));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ffmpeg did not shut down in time for {Source}", _description);
        }

        _process.Dispose();
        _cts.Dispose();
    }

    // ffmpeg echoes the input URL in its errors
    private string Redact(string text) =>
        text.Replace(_url, _description, StringComparison.Ordinal);

    // Stream URLs often carry credentials (e.g. Subsonic tokens) in the query string; keep them out of logs
    private static string DescribeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path)
            : "<invalid url>";
}
