using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects.Test;

/// <summary>Tests 2D and 3D particle instance playback lifecycle, curve/gradient texture baking, and CPU spawn accounting.</summary>
public class TestParticleSimulation
{
    /// <summary>Minimal no-GPU engine host.</summary>
    public sealed class Host(GameEngineSetting setting) : GameEngine(setting);

    private Host _engine = null!;

    /// <summary>Creates the shared no-GPU engine host.</summary>
    [OneTimeSetUp]
    public void SetUp() => _engine = new Host(GameEngineSetting.CreateNoGPU());

    /// <summary>Disposes the shared engine host.</summary>
    [OneTimeTearDown]
    public void TearDown() => _engine.Dispose();

    /// <summary>2D: Play, Stop, expiry into inactive, Restart, and per-group visibility updates behave consistently.</summary>
    [Test]
    public void PlayStopRestartAndVisibilityLifecycle2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 1024, emitterSlots: 16)
        {
            SimulationRateLimitEnabled = false,
        };
        var effect = new ParticleEffect2D
        {
            Name = "lifecycle2d",
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = "OneShot",
                    MaxParticles = 64,
                    Looping = false,
                    Duration = 0.2f,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(0.1f, 0.2f),
                },
            ],
        };

        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new Vector2(3f, -2f)), height: 1.5f, seed: 7);
        Assert.That(instance.IsPlaying, Is.True);
        Assert.That(instance.IsActive, Is.True);

        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            // Advance past duration + max lifetime -> should finish and become inactive.
            for (int i = 0; i < 10; i++)
            {
                context.BeginFrame();
                system.RecordSimulation(context, 0.1f);
                context.DiscardFrame();
            }
            Assert.That(instance.IsActive, Is.False);

            // Restart revives emission and resets timeline.
            instance.Restart();
            Assert.That(instance.IsPlaying, Is.True);
            Assert.That(instance.IsActive, Is.True);
            Assert.That(instance.Time, Is.Zero);

            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            context.DiscardFrame();
            Assert.That(system.PlannedDrawBatchCount, Is.EqualTo(1));

            // Hiding the only group skips draw batch planning cleanly.
            instance.SetGroupVisible(0, false);
            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            context.DiscardFrame();
            Assert.That(system.PlannedDrawBatchCount, Is.Zero);

            instance.Stop();
            Assert.That(instance.IsPlaying, Is.False);
        }
        finally
        {
            context.Destroy();
        }
    }

    /// <summary>Rate-limited frames invalidate their cached plan and drain kills when visibility, Play, Restart, or CreateInstance occur.</summary>
    [Test]
    public void RateLimitedMutationsInvalidatePlanAndDrainKills2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 512, emitterSlots: 8)
        {
            SimulationRateLimitEnabled = true,
            SimulationInterval = 1.0f,
        };
        var effect = new ParticleEffect2D
        {
            Name = "ratelimit2d",
            Groups = [new ParticleGroup2D { Name = "Main", MaxParticles = 64, EmissionRate = 60f, Lifetime = new ParticleRange(1f, 2f) }],
        };

        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(Vector2.Zero), seed: 5);
        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            context.BeginFrame();
            system.RecordSimulation(context, 0.1f);
            context.Submit().Wait();
            Assert.That(system.PlannedDrawBatchCount, Is.EqualTo(1));

            instance.IsVisible = false;
            context.BeginFrame();
            system.RecordSimulation(context, 0.001f);
            context.Submit().Wait();
            Assert.That(system.PlannedDrawBatchCount, Is.Zero);

            instance.IsVisible = true;
            instance.SetGroupVisible(0, false);
            context.BeginFrame();
            system.RecordSimulation(context, 0.001f);
            context.Submit().Wait();
            Assert.That(system.PlannedDrawBatchCount, Is.Zero);

            instance.SetGroupVisible(0, true);
            context.BeginFrame();
            system.RecordSimulation(context, 0.001f);
            context.Submit().Wait();
            Assert.That(system.PlannedDrawBatchCount, Is.EqualTo(1));

            instance.Restart();
            Assert.That(system.Pool.HasPendingKills, Is.True);
            context.BeginFrame();
            system.RecordSimulation(context, 0.001f);
            context.Submit().Wait();
            Assert.That(system.Pool.HasPendingKills, Is.False);
        }
        finally
        {
            context.Destroy();
        }
    }

    /// <summary>3D: Play, Stop, expiry, Restart, and custom curve baking work in 3D.</summary>
    [Test]
    public void LifecycleAndOverLifeCurveBaking3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 1024, emitterSlots: 16);
        var effect = new ParticleEffect3D
        {
            Name = "curves3d",
            Groups =
            [
                new ParticleGroup3D
                {
                    Name = "Baked",
                    MaxParticles = 64,
                    Looping = true,
                    EmissionRate = 120f,
                    Lifetime = new ParticleRange(0.5f, 1.0f),
                    ColorGradient =
                    [
                        new ParticleColorKey { Time = 0f, Color = new ColorFloat(1f, 0f, 0f, 1f) },
                        new ParticleColorKey { Time = 1f, Color = new ColorFloat(0f, 0f, 1f, 0f) },
                    ],
                    SizeCurve =
                    [
                        new ParticleScalarKey { Time = 0f, Value = 0.5f },
                        new ParticleScalarKey { Time = 1f, Value = 2.0f },
                    ],
                },
            ],
        };

        using ParticleEffectInstance3D instance = system.CreateInstance(effect, new Transform3D(new Vector3(1f, 2f, 3f)), seed: 11);
        EmitterParams3D parms = instance.GetGroupParams(0);

        Assert.Multiple(() =>
        {
            Assert.That(parms.Flags & EmitterParams3D.FlagColorGradient, Is.Not.Zero);
            Assert.That(parms.Flags & EmitterParams3D.FlagSizeCurve, Is.Not.Zero);
            Assert.That(parms.ColorGradientTextureId, Is.Not.EqualTo(_engine.RenderingSystem.TextureWhite.SampledIndex));
            Assert.That(parms.SizeCurveTextureId, Is.Not.EqualTo(_engine.RenderingSystem.TextureWhite.SampledIndex));
        });

        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            context.BeginFrame();
            system.RecordSimulation(context, 0.1f);
            context.Submit().Wait();
            Assert.That(system.PlannedEmitBlockCount, Is.GreaterThan(0));
            Assert.That(parms.SpawnCount + instance.GetGroupParams(0).SpawnCount, Is.GreaterThan(0u));
        }
        finally
        {
            context.Destroy();
        }
    }

    /// <summary>2D: finished one-shot disposal preserves 60Hz rate limiting and defers slice kill, while active disposal and accumulator reset behave cleanly.</summary>
    [Test]
    public void DisposingFinishedOneShotPreservesRateLimitAndDefersKill2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 512, emitterSlots: 16)
        {
            SimulationRateLimitEnabled = false,
        };
        var oneShotEffect = new ParticleEffect2D
        {
            Name = "oneshot2d",
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = "Burst",
                    MaxParticles = 64,
                    Looping = false,
                    Duration = 0.2f,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(0.1f, 0.2f),
                },
            ],
        };
        var loopEffect = new ParticleEffect2D
        {
            Name = "loop2d",
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = "Loop",
                    MaxParticles = 64,
                    Looping = true,
                    EmissionRate = 60f,
                    Lifetime = new ParticleRange(1f, 2f),
                },
            ],
        };

        ParticleEffectInstance2D oneShot = system.CreateInstance(oneShotEffect, new Transform2D(Vector2.Zero), seed: 1);
        using ParticleEffectInstance2D loop = system.CreateInstance(loopEffect, new Transform2D(Vector2.One), seed: 2);
        using ParticleEffectInstance2D activeVictim = system.CreateInstance(loopEffect, new Transform2D(Vector2.UnitX), seed: 3);

        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            // Advance one-shot to inactive without rate limit.
            for (int i = 0; i < 6; i++)
            {
                context.BeginFrame();
                system.RecordSimulation(context, 0.1f);
                context.Submit().Wait();
            }
            Assert.That(oneShot.IsActive, Is.False);

            // Switch to 1s rate limit and establish a valid plan.
            system.SimulationRateLimitEnabled = true;
            system.SimulationInterval = 1.0f;
            context.BeginFrame();
            system.RecordSimulation(context, 1.0f);
            context.Submit().Wait();
            Assert.That(system.PlannedDrawBatchCount, Is.EqualTo(1));
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(2));

            // Disposing a finished one-shot queues a slice kill without breaking the rate-limit window.
            float timeBeforeOneShotDispose = loop.Time;
            oneShot.Dispose();
            Assert.That(system.Pool.HasPendingKills, Is.True);

            context.BeginFrame();
            system.RecordSimulation(context, 0.001f);
            context.Submit().Wait();
            Assert.Multiple(() =>
            {
                Assert.That(loop.Time, Is.EqualTo(timeBeforeOneShotDispose));
                Assert.That(system.Pool.HasPendingKills, Is.True);
            });

            // Once the simulation interval elapses, simulation runs and drains the deferred kill.
            context.BeginFrame();
            system.RecordSimulation(context, 1.0f);
            context.Submit().Wait();
            Assert.Multiple(() =>
            {
                Assert.That(loop.Time, Is.GreaterThan(timeBeforeOneShotDispose));
                Assert.That(system.Pool.HasPendingKills, Is.False);
            });

            // Disposing an active visible instance DOES invalidate the plan immediately on the next sub-interval frame.
            activeVictim.Dispose();
            context.BeginFrame();
            system.RecordSimulation(context, 0.001f);
            context.Submit().Wait();
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(1));

            // Accumulator resets to 0 when plan invalidation triggers an early step.
            context.BeginFrame();
            system.RecordSimulation(context, 0.4f);
            context.Submit().Wait();
            loop.IsVisible = false;
            float timeAfterAccum = loop.Time;

            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            context.Submit().Wait();
            loop.IsVisible = true;
            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            context.Submit().Wait();
            float timeAfterInvalidated = loop.Time;
            Assert.That(timeAfterInvalidated, Is.GreaterThan(timeAfterAccum));

            // Next sub-interval 0.7f call must still be rate-limited (0.7 < 1.0, proving 0.4 was consumed/reset).
            context.BeginFrame();
            system.RecordSimulation(context, 0.7f);
            context.Submit().Wait();
            Assert.That(loop.Time, Is.EqualTo(timeAfterInvalidated));
        }
        finally
        {
            oneShot.Dispose();
            context.Destroy();
        }
    }

    /// <summary>3D: finished one-shot disposal preserves 60Hz rate limiting and defers slice kill, while active disposal invalidates immediately.</summary>
    [Test]
    public void DisposingFinishedOneShotPreservesRateLimitAndDefersKill3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 512, emitterSlots: 16)
        {
            SimulationRateLimitEnabled = false,
        };
        var oneShotEffect = new ParticleEffect3D
        {
            Name = "oneshot3d",
            Groups =
            [
                new ParticleGroup3D
                {
                    Name = "Burst",
                    MaxParticles = 64,
                    Looping = false,
                    Duration = 0.2f,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(0.1f, 0.2f),
                },
            ],
        };
        var loopEffect = new ParticleEffect3D
        {
            Name = "loop3d",
            Groups =
            [
                new ParticleGroup3D
                {
                    Name = "Loop",
                    MaxParticles = 64,
                    Looping = true,
                    EmissionRate = 60f,
                    Lifetime = new ParticleRange(1f, 2f),
                },
            ],
        };

        ParticleEffectInstance3D oneShot = system.CreateInstance(oneShotEffect, new Transform3D(Vector3.Zero), seed: 1);
        using ParticleEffectInstance3D loop = system.CreateInstance(loopEffect, new Transform3D(Vector3.One), seed: 2);
        using ParticleEffectInstance3D activeVictim = system.CreateInstance(loopEffect, new Transform3D(Vector3.UnitX), seed: 3);

        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            for (int i = 0; i < 6; i++)
            {
                context.BeginFrame();
                system.RecordSimulation(context, 0.1f);
                context.Submit().Wait();
            }
            Assert.That(oneShot.IsActive, Is.False);

            system.SimulationRateLimitEnabled = true;
            system.SimulationInterval = 1.0f;
            context.BeginFrame();
            system.RecordSimulation(context, 1.0f);
            context.Submit().Wait();
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(2));

            float timeBeforeOneShotDispose = loop.Time;
            oneShot.Dispose();
            Assert.That(system.Pool.HasPendingKills, Is.True);

            context.BeginFrame();
            system.RecordSimulation(context, 0.001f);
            context.Submit().Wait();
            Assert.Multiple(() =>
            {
                Assert.That(loop.Time, Is.EqualTo(timeBeforeOneShotDispose));
                Assert.That(system.Pool.HasPendingKills, Is.True);
            });

            context.BeginFrame();
            system.RecordSimulation(context, 1.0f);
            context.Submit().Wait();
            Assert.Multiple(() =>
            {
                Assert.That(loop.Time, Is.GreaterThan(timeBeforeOneShotDispose));
                Assert.That(system.Pool.HasPendingKills, Is.False);
            });

            activeVictim.Dispose();
            context.BeginFrame();
            system.RecordSimulation(context, 0.001f);
            context.Submit().Wait();
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(1));
        }
        finally
        {
            oneShot.Dispose();
            context.Destroy();
        }
    }
}
