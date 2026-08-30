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

    private uint AdvanceLifecycle(ref float time, ref float accumulator, ref float idleTimer,
        float dt, bool playing, float rate, float duration, bool looping, float maxLifetime, out bool active)
        => ParticleEmission.AdvanceLifecycle(ref time, ref accumulator, ref idleTimer, dt, playing,
            rate, duration, looping, [], ref _random, 4096, maxLifetime, out active);

    [Test]
    public void OneShotGroupDeactivatesAfterDurationPlusMaxLifetime()
    {
        // Regression: a finished one-shot (non-looping, duration reached) used to stay
        // active forever because the per-frame gate only looked at the instance-wide
        // playing flag — finished explosions were never destroyed.
        // 1 s one-shot, max particle lifetime 0.5 s at 60 fps: emission ends around
        // frame 60, the group must deactivate around frame 60 + 0.6 s * 60 ≈ 97.
        float time = 0f, accumulator = 0f, idleTimer = 0f;
        const float dt = 1f / 60f;
        int deactivatedFrame = -1;
        uint earlySpawns = 0, lateSpawns = 0;
        for (int frame = 0; frame < 300; frame++)
        {
            uint spawn = AdvanceLifecycle(ref time, ref accumulator, ref idleTimer, dt,
                playing: true, rate: 100f, duration: 1f, looping: false, maxLifetime: 0.5f, out bool active);
            if (frame < 60)
            {
                earlySpawns += spawn;
            }
            else
            {
                lateSpawns += spawn;
            }
            if (!active && deactivatedFrame < 0)
            {
                deactivatedFrame = frame;
            }
        }
        Assert.Multiple(() =>
        {
            Assert.That(earlySpawns, Is.GreaterThan(0u), "the one-shot must emit during its duration");
            Assert.That(lateSpawns, Is.EqualTo(0u), "no spawns after the timeline ended");
            Assert.That(deactivatedFrame, Is.InRange(90, 110), "deactivation ≈ duration + maxLifetime");
            // Once emission ends the timeline stops advancing (EmitterTime stays stable).
            Assert.That(time, Is.LessThan(1f + 3f * dt));
        });
    }

    [Test]
    public void LoopingGroupStaysActiveWhilePlaying()
    {
        // No deactivation regression for looping emitters: 5 s of a 1 s loop.
        float time = 0f, accumulator = 0f, idleTimer = 0f;
        uint total = 0;
        for (int frame = 0; frame < 300; frame++)
        {
            total += AdvanceLifecycle(ref time, ref accumulator, ref idleTimer, 1f / 60f,
                playing: true, rate: 50f, duration: 1f, looping: true, maxLifetime: 0.5f, out bool active);
            Assert.That(active, Is.True, $"frame {frame}");
        }
        Assert.That(total, Is.EqualTo(250u).Within(4u));
    }

    [Test]
    public void StoppedGroupIdlesOutAfterMaxLifetime()
    {
        // The pre-existing stop path is unchanged: an infinite emitter that stops
        // playing deactivates once every particle had time to die (0.5 + 0.1 s).
        float time = 0f, accumulator = 0f, idleTimer = 0f;
        const float dt = 1f / 60f;
        for (int frame = 0; frame < 30; frame++)
        {
            AdvanceLifecycle(ref time, ref accumulator, ref idleTimer, dt,
                playing: true, rate: 100f, duration: 0f, looping: false, maxLifetime: 0.5f, out bool active);
            Assert.That(active, Is.True, $"playing frame {frame}");
        }
        int deactivatedFrame = -1;
        for (int frame = 30; frame < 120; frame++)
        {
            AdvanceLifecycle(ref time, ref accumulator, ref idleTimer, dt,
                playing: false, rate: 100f, duration: 0f, looping: false, maxLifetime: 0.5f, out bool active);
            if (!active && deactivatedFrame < 0)
            {
                deactivatedFrame = frame;
            }
        }
        Assert.That(deactivatedFrame, Is.InRange(60, 80), "deactivation ≈ stop frame + maxLifetime + 0.1 s");
    }
}
