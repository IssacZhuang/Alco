using NUnit.Framework;

namespace Alco.Particles.Test;

/// <summary>
/// Tests of the over-life lookup baking (<see cref="ParticleOverLifeBake"/>):
/// key sorting, clamped ends and linear interpolation of the color gradient and
/// the size curve, plus the RGBA8/R16Float sample-row baking.
/// </summary>
public class TestParticleOverLifeBake
{
    private static List<ParticleColorKey> GradientKeys(params (float Time, ColorFloat Color)[] keys)
        => [.. keys.Select(key => new ParticleColorKey { Time = key.Time, Color = key.Color })];

    private static List<ParticleScalarKey> CurveKeys(params (float Time, float Value)[] keys)
        => [.. keys.Select(key => new ParticleScalarKey { Time = key.Time, Value = key.Value })];

    [Test]
    public void GradientEndpointsAndMidpointInterpolate()
    {
        List<ParticleColorKey> keys = GradientKeys(
            (0f, new ColorFloat(1f, 0f, 0f, 1f)),
            (1f, new ColorFloat(0f, 0f, 1f, 0f)));

        Assert.Multiple(() =>
        {
            Assert.That(ParticleOverLifeBake.EvaluateGradient(keys, 0f), Is.EqualTo(new ColorFloat(1f, 0f, 0f, 1f)));
            Assert.That(ParticleOverLifeBake.EvaluateGradient(keys, 1f), Is.EqualTo(new ColorFloat(0f, 0f, 1f, 0f)));
            ColorFloat mid = ParticleOverLifeBake.EvaluateGradient(keys, 0.5f);
            Assert.That(mid.R, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(mid.B, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(mid.A, Is.EqualTo(0.5f).Within(1e-6));
        });
    }

    [Test]
    public void GradientClampsOutsideTheKeyRangeAndSortsUnsortedKeys()
    {
        // Authored out of order, with times outside [0, 1] and a mid key.
        List<ParticleColorKey> keys = GradientKeys(
            (2f, new ColorFloat(0f, 0f, 0f, 0f)),   // clamps to t = 1
            (-1f, new ColorFloat(1f, 1f, 1f, 1f)),  // clamps to t = 0
            (0.5f, new ColorFloat(0f, 1f, 0f, 1f)));

        Assert.Multiple(() =>
        {
            Assert.That(ParticleOverLifeBake.EvaluateGradient(keys, 0f), Is.EqualTo(new ColorFloat(1f, 1f, 1f, 1f)));
            Assert.That(ParticleOverLifeBake.EvaluateGradient(keys, 1f), Is.EqualTo(new ColorFloat(0f, 0f, 0f, 0f)));
            Assert.That(ParticleOverLifeBake.EvaluateGradient(keys, 0.5f), Is.EqualTo(new ColorFloat(0f, 1f, 0f, 1f)));
            // Halfway between the white and the green key: red lerps out, green holds.
            ColorFloat quarter = ParticleOverLifeBake.EvaluateGradient(keys, 0.25f);
            Assert.That(quarter.R, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(quarter.G, Is.EqualTo(1f).Within(1e-6));
        });
    }

    [Test]
    public void CurveEndpointsClampAndValuesAboveOneSurvive()
    {
        List<ParticleScalarKey> keys = CurveKeys((0.25f, 0.5f), (0.75f, 3.5f));

        Assert.Multiple(() =>
        {
            // Outside the key range the nearest end key holds (clamped ends).
            Assert.That(ParticleOverLifeBake.EvaluateCurve(keys, 0f), Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(ParticleOverLifeBake.EvaluateCurve(keys, 1f), Is.EqualTo(3.5f).Within(1e-6));
            Assert.That(ParticleOverLifeBake.EvaluateCurve(keys, 0.5f), Is.EqualTo(2f).Within(1e-6));
        });
    }

    [Test]
    public void SingleKeyIsConstantAndDuplicateTimesTakeTheLaterKey()
    {
        List<ParticleScalarKey> single = CurveKeys((0.4f, 2f));
        Assert.That(ParticleOverLifeBake.EvaluateCurve(single, 0.1f), Is.EqualTo(2f).Within(1e-6));

        List<ParticleScalarKey> duplicate = CurveKeys((0.5f, 1f), (0.5f, 2f));
        Assert.That(ParticleOverLifeBake.EvaluateCurve(duplicate, 0.5f), Is.EqualTo(2f).Within(1e-6));
    }

    [Test]
    public void EmptyKeyListsThrow()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => ParticleOverLifeBake.EvaluateGradient([], 0.5f));
            Assert.Throws<ArgumentException>(() => ParticleOverLifeBake.EvaluateCurve([], 0.5f));
        });
    }

    [Test]
    public void BakeGradientFillsRgba8RowsWithExactEndpoints()
    {
        List<ParticleColorKey> keys = GradientKeys(
            (0f, new ColorFloat(1f, 1f, 1f, 1f)),
            (1f, new ColorFloat(1f, 0f, 0f, 0f)));

        byte[] pixels = new byte[ParticleOverLifeBake.TextureWidth * 4];
        ParticleOverLifeBake.BakeGradient(keys, pixels);

        Assert.Multiple(() =>
        {
            // The first texel is the first key exactly, the last the last.
            Assert.That(pixels.AsSpan(0, 4).ToArray(), Is.EqualTo(new byte[] { 255, 255, 255, 255 }));
            Assert.That(pixels.AsSpan((ParticleOverLifeBake.TextureWidth - 1) * 4, 4).ToArray(),
                Is.EqualTo(new byte[] { 255, 0, 0, 0 }));
            // The middle texel sits halfway between the keys (+- quantization).
            Assert.That(pixels[(ParticleOverLifeBake.TextureWidth / 2) * 4 + 3], Is.EqualTo((byte)128).Within(2));
        });

        // A two-texel row holds exactly the two end keys.
        byte[] tiny = new byte[8];
        ParticleOverLifeBake.BakeGradient(keys, tiny);
        Assert.That(tiny, Is.EqualTo(new byte[] { 255, 255, 255, 255, 255, 0, 0, 0 }));
    }

    [Test]
    public void BakeCurveFillsR16RowsWithExactEndpoints()
    {
        List<ParticleScalarKey> keys = CurveKeys((0f, 0.25f), (1f, 3.5f));

        Half[] texels = new Half[ParticleOverLifeBake.TextureWidth];
        ParticleOverLifeBake.BakeCurve(keys, texels);

        Assert.Multiple(() =>
        {
            Assert.That((float)texels[0], Is.EqualTo(0.25f).Within(1e-3));
            Assert.That((float)texels[^1], Is.EqualTo(3.5f).Within(1e-2)); // half precision
            Assert.That((float)texels[ParticleOverLifeBake.TextureWidth / 2], Is.EqualTo(1.875f).Within(2e-2));
        });
    }
}
