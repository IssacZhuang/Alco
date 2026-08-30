using System.Numerics;

namespace Alco.Particles;

/// <summary>
/// Bakes the over-life tables of a particle group (the color gradient and the
/// size curve) into the sample arrays of the 1D lookup textures the render pass
/// vertex shaders sample by normalized particle age. Pure CPU math — key
/// sorting, clamped ends, linear interpolation — kept separate from the GPU
/// texture upload (<see cref="GpuParticleSystem2D"/> / <see cref="GpuParticleSystem3D"/>)
/// so it is unit-testable without a device.
/// </summary>
public static class ParticleOverLifeBake
{
    /// <summary>The width of the baked lookup textures in texels.</summary>
    public const int TextureWidth = 256;

    /// <summary>
    /// Evaluates a color gradient at a normalized age: the keys, sorted by time
    /// and clamped to [0, 1], interpolate linearly (component-wise); before the
    /// first key and after the last the value clamps to the end key.
    /// </summary>
    /// <param name="keys">The gradient keys (any order, times may lie outside [0, 1]).</param>
    /// <param name="age01">The normalized particle age (clamped to [0, 1]).</param>
    /// <returns>The interpolated gradient color.</returns>
    public static ColorFloat EvaluateGradient(IReadOnlyList<ParticleColorKey> keys, float age01)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("A color gradient needs at least one key.", nameof(keys));
        }
        (float Time, ColorFloat Value)[] sorted = SortKeys(keys);
        float t = Math.Clamp(age01, 0f, 1f);
        if (t < sorted[0].Time)
        {
            return sorted[0].Value;
        }
        if (t >= sorted[^1].Time)
        {
            return sorted[^1].Value;
        }
        for (int i = 0; i < sorted.Length - 1; i++)
        {
            if (t < sorted[i + 1].Time)
            {
                float span = sorted[i + 1].Time - sorted[i].Time;
                if (span <= 1e-6f) // duplicate key times: the later key wins
                {
                    return sorted[i + 1].Value;
                }
                float f = (t - sorted[i].Time) / span;
                return Vector4.Lerp(sorted[i].Value.value, sorted[i + 1].Value.value, f);
            }
        }
        return sorted[^1].Value;
    }

    /// <summary>
    /// Evaluates a scalar curve at a normalized age; the same key rules as
    /// <see cref="EvaluateGradient"/> apply.
    /// </summary>
    /// <param name="keys">The curve keys (any order, times may lie outside [0, 1]).</param>
    /// <param name="age01">The normalized particle age (clamped to [0, 1]).</param>
    /// <returns>The interpolated curve value.</returns>
    public static float EvaluateCurve(IReadOnlyList<ParticleScalarKey> keys, float age01)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("A size curve needs at least one key.", nameof(keys));
        }
        (float Time, float Value)[] sorted = SortKeys(keys);
        float t = Math.Clamp(age01, 0f, 1f);
        if (t < sorted[0].Time)
        {
            return sorted[0].Value;
        }
        if (t >= sorted[^1].Time)
        {
            return sorted[^1].Value;
        }
        for (int i = 0; i < sorted.Length - 1; i++)
        {
            if (t < sorted[i + 1].Time)
            {
                float span = sorted[i + 1].Time - sorted[i].Time;
                if (span <= 1e-6f) // duplicate key times: the later key wins
                {
                    return sorted[i + 1].Value;
                }
                float f = (t - sorted[i].Time) / span;
                return sorted[i].Value + (sorted[i + 1].Value - sorted[i].Value) * f;
            }
        }
        return sorted[^1].Value;
    }

    /// <summary>
    /// Bakes a color gradient into one row of RGBA8 pixels (4 bytes per texel):
    /// texel <c>i</c> of <c>n</c> holds <see cref="EvaluateGradient"/> at
    /// <c>i / (n - 1)</c>, so the texture's clamped linear sampling hits the end
    /// keys exactly at ages 0 and 1.
    /// </summary>
    /// <param name="keys">The gradient keys (any order).</param>
    /// <param name="rgba8">The pixel row to fill; the texel count is <c>rgba8.Length / 4</c>.</param>
    public static void BakeGradient(IReadOnlyList<ParticleColorKey> keys, Span<byte> rgba8)
    {
        int width = rgba8.Length / 4;
        for (int i = 0; i < width; i++)
        {
            ColorFloat color = EvaluateGradient(keys, TexelAge(i, width));
            Color32 pixel = color.ToColor32();
            rgba8[i * 4 + 0] = pixel.R;
            rgba8[i * 4 + 1] = pixel.G;
            rgba8[i * 4 + 2] = pixel.B;
            rgba8[i * 4 + 3] = pixel.A;
        }
    }

    /// <summary>
    /// Bakes a scalar curve into one row of R16Float texels: texel <c>i</c> of
    /// <c>n</c> holds <see cref="EvaluateCurve"/> at <c>i / (n - 1)</c> (see
    /// <see cref="BakeGradient"/>). A float texture because curve values may
    /// exceed 1 (growth), which a unorm texture cannot represent.
    /// </summary>
    /// <param name="keys">The curve keys (any order).</param>
    /// <param name="r16">The texel row to fill; the texel count is <c>r16.Length</c>.</param>
    public static void BakeCurve(IReadOnlyList<ParticleScalarKey> keys, Span<Half> r16)
    {
        for (int i = 0; i < r16.Length; i++)
        {
            r16[i] = (Half)EvaluateCurve(keys, TexelAge(i, r16.Length));
        }
    }

    // The normalized age of texel i of n (endpoints exact at 0 and 1).
    private static float TexelAge(int i, int width) => width > 1 ? (float)i / (width - 1) : 0f;

    // Copies the keys sorted by (time, authored index) so duplicate times keep
    // their authored order (Array.Sort is not stable on its own); the times are
    // clamped to [0, 1].
    private static (float Time, ColorFloat Value)[] SortKeys(IReadOnlyList<ParticleColorKey> keys)
    {
        var sorted = new ((float Time, ColorFloat Value) Key, int Index)[keys.Count];
        for (int i = 0; i < sorted.Length; i++)
        {
            sorted[i] = ((Math.Clamp(keys[i].Time, 0f, 1f), keys[i].Color), i);
        }
        Array.Sort(sorted, static (a, b) => a.Key.Time != b.Key.Time
            ? a.Key.Time.CompareTo(b.Key.Time)
            : a.Index.CompareTo(b.Index));
        var result = new (float Time, ColorFloat Value)[keys.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = sorted[i].Key;
        }
        return result;
    }

    private static (float Time, float Value)[] SortKeys(IReadOnlyList<ParticleScalarKey> keys)
    {
        var sorted = new ((float Time, float Value) Key, int Index)[keys.Count];
        for (int i = 0; i < sorted.Length; i++)
        {
            sorted[i] = ((Math.Clamp(keys[i].Time, 0f, 1f), keys[i].Value), i);
        }
        Array.Sort(sorted, static (a, b) => a.Key.Time != b.Key.Time
            ? a.Key.Time.CompareTo(b.Key.Time)
            : a.Index.CompareTo(b.Index));
        var result = new (float Time, float Value)[keys.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = sorted[i].Key;
        }
        return result;
    }
}
