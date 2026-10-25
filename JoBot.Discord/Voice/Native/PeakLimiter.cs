namespace JoBot.Discord.Voice.Native;

/// <summary>
/// Keeps mixed audio below full scale without the harsh distortion of hard clipping.
/// </summary>
/// <remarks>
/// Works on one 20 ms frame at a time, which doubles as a one-frame lookahead: the frame's peak is known
/// before any gain is applied, so gain reduction can kick in before the loud part rather than after it.
/// Gain drops immediately when needed and recovers gradually, so it doesn't pump on every transient.
/// A soft-knee clipper catches anything that still exceeds the ceiling.
/// </remarks>
public sealed class PeakLimiter
{
    // In normalised sample units (1.0 = full scale). The clipper's knee starts at the ceiling, so audio the
    // limiter has already brought down passes through untouched and only true overs are shaped.
    private const float Ceiling = 0.891f; // -1 dBFS
    // Fraction of the remaining gain reduction recovered per frame (~90% recovered after 500 ms)
    private const float ReleasePerFrame = 0.08f;

    private float _gain = 1f;

    /// <summary>Current gain reduction, 1.0 when not limiting.</summary>
    public float Gain => _gain;

    /// <summary>Limits <paramref name="samples"/> (full scale = ±32768) and writes 16-bit output.</summary>
    public void Process(ReadOnlySpan<float> samples, Span<short> output)
    {
        var peak = 0f;
        foreach (var sample in samples)
            peak = Math.Max(peak, Math.Abs(sample));
        peak /= 32768f;

        var required = peak > Ceiling ? Ceiling / peak : 1f;

        // Ramp from the previous frame's gain so changes are click-free. When reducing, the ramp means the
        // first samples get slightly more gain than required; the soft clipper absorbs that.
        var startGain = _gain;
        var endGain = required < _gain
            ? required
            : _gain + (Math.Min(1f, required) - _gain) * ReleasePerFrame;

        var step = (endGain - startGain) / samples.Length;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i] / 32768f * (startGain + step * i);
            output[i] = (short)Math.Clamp(SoftClip(value) * 32767f, short.MinValue, short.MaxValue);
        }

        _gain = endGain;
    }

    /// <summary>
    /// Linear up to <see cref="Ceiling"/>, then bends smoothly towards full scale. The curve is continuous
    /// and has matching slope at the knee, so there is no audible corner.
    /// </summary>
    private static float SoftClip(float value)
    {
        var magnitude = Math.Abs(value);
        if (magnitude <= Ceiling)
            return value;

        const float headroom = 1f - Ceiling;
        var shaped = Ceiling + headroom * MathF.Tanh((magnitude - Ceiling) / headroom);
        return MathF.CopySign(shaped, value);
    }
}
