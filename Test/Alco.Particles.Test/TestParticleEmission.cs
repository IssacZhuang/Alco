using NUnit.Framework;
using Alco.Particles;

namespace Alco.Particles.Test;

/// <summary>
/// CPU-side emission bookkeeping tests: rate accumulation, burst timing (single
/// and looping timelines), the capacity clamp and the emission window of
/// non-looping emitters.
/// </summary>
public class TestParticleEmission
{
    private FastRandom _random = new(42);

    private uint Advance(ref float time, ref float accumulator, float dt, float rate,
        float duration = 0f, bool looping = false, IReadOnlyList<ParticleBurst>? bursts = null, uint capacity = 4096)
        => ParticleEmission.Advance(ref time, ref accumulator, dt, rate, duration, looping,
            bursts ?? [], ref _random, capacity);

    [Test]
    public void ContinuousRateAccumulatesFractionally()
    {
        float time = 0f, accumulator = 0f;
        // 10 particles/s at 60 fps: 0.1667 per frame → 10 particles over 60 frames.
        uint total = 0;
        for (int i = 0; i < 60; i++)
        {
            total += Advance(ref time, ref accumulator, 1f / 60f, 10f);
        }
        Assert.That(total, Is.EqualTo(10u).Within(1u));
    }

    [Test]
    public void HitchBurstIsClampedToOneSecond()
    {
        float time = 0f, accumulator = 0f;
        // A 5 s hitch at 100 particles/s must not spawn 500 particles at once.
        uint spawn = Advance(ref time, ref accumulator, 5f, 100f);
        Assert.That(spawn, Is.LessThanOrEqualTo(100u));
    }

    [Test]
    public void BurstFiresOnceAtStartOfInfiniteTimeline()
    {
        float time = 0f, accumulator = 0f;
        var bursts = new List<ParticleBurst> { new() { Time = 0f, CountMin = 20, CountMax = 20 } };
        uint first = Advance(ref time, ref accumulator, 1f / 60f, 0f, bursts: bursts);
        uint second = Advance(ref time, ref accumulator, 1f / 60f, 0f, bursts: bursts);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(20u));
            Assert.That(second, Is.EqualTo(0u));
        });
    }

    [Test]
    public void BurstRefiresEveryLoopCycle()
    {
        float time = 0f, accumulator = 0f;
        var bursts = new List<ParticleBurst> { new() { Time = 0.5f, CountMin = 8, CountMax = 8 } };
        uint total = 0;
        // Two 1-second looping cycles at 60 fps.
        for (int i = 0; i < 120; i++)
        {
            total += Advance(ref time, ref accumulator, 1f / 60f, 0f, duration: 1f, looping: true, bursts: bursts);
        }
        Assert.That(total, Is.EqualTo(16u));
    }

    [Test]
    public void NonLoopingEmitterStopsAfterDuration()
    {
        float time = 0f, accumulator = 0f;
        // Run 2 seconds of a 1-second non-looping emitter at 60 fps; the second
        // half must emit nothing.
        uint firstHalf = 0, secondHalf = 0;
        for (int i = 0; i < 120; i++)
        {
            uint spawn = Advance(ref time, ref accumulator, 1f / 60f, 100f, duration: 1f, looping: false);
            if (i < 60)
            {
                firstHalf += spawn;
            }
            else
            {
                secondHalf += spawn;
            }
        }
        Assert.Multiple(() =>
        {
            Assert.That(firstHalf, Is.GreaterThan(0u));
            Assert.That(secondHalf, Is.EqualTo(0u));
        });
    }

    [Test]
    public void SpawnCountClampsToCapacity()
    {
        float time = 0f, accumulator = 0f;
        uint spawn = Advance(ref time, ref accumulator, 1f, 100000f, capacity: 64);
        Assert.That(spawn, Is.EqualTo(64u));
    }
}
