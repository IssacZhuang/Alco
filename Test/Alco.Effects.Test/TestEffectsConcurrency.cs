using System.Collections.Concurrent;
using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Effects.Test;

/// <summary>
/// Stress tests verifying thread-safe instance lifecycle, material module registration, and
/// concurrent rendering across <see cref="GpuParticleSystem2D"/>, <see cref="GpuParticleSystem3D"/>,
/// <see cref="GpuTrailSystem2D"/>, and <see cref="GpuTrailSystem3D"/> on <see cref="GraphicsBackend.NoGPU"/>.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class TestEffectsConcurrency
{
    private sealed class CountingModule : IParticleMaterialModule
    {
        public void ConfigureMaterial(Material material)
        {
            _ = material.Root;
        }
    }

    /// <summary>Worker threads create/mutate/dispose 2D particle instances while the render thread simulates and renders.</summary>
    [Test]
    public void ParticleSystem2D_ConcurrentLifecycleAndRender_IsThreadSafe()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuParticleSystem2D(host.RenderingSystem, host.Shaders, particleCapacity: 2048, emitterSlots: 64)
        {
            SimulationRateLimitEnabled = false,
        };
        using Texture target = host.CreateColorTarget(32, 32);
        using var context = new RenderContext(host.RenderingSystem);

        ParticleEffect2D[] effects =
        [
            new()
            {
                Name = "c2d_a",
                Groups =
                [
                    new ParticleGroup2D
                    {
                        Name = "G0",
                        MaxParticles = 64,
                        EmissionRate = 30f,
                        Lifetime = new ParticleRange(0.2f, 0.5f),
                        ColorGradient = [new ParticleColorKey { Time = 0f, Color = ColorFloat.White }, new ParticleColorKey { Time = 1f, Color = ColorFloat.Transparent }],
                        SizeCurve = [new ParticleScalarKey { Time = 0f, Value = 1f }, new ParticleScalarKey { Time = 1f, Value = 0f }],
                    },
                ],
            },
            new()
            {
                Name = "c2d_b",
                Groups =
                [
                    new ParticleGroup2D
                    {
                        Name = "G1",
                        MaxParticles = 64,
                        EmissionRate = 45f,
                        Blend = BlendState.Additive,
                        Lifetime = new ParticleRange(0.2f, 0.4f),
                    },
                ],
            },
        ];

        using var startGate = new ManualResetEventSlim(false);
        using var workersDone = new ManualResetEventSlim(false);
        var errors = new ConcurrentQueue<Exception>();

        Task[] workers = new Task[4];
        for (int w = 0; w < workers.Length; w++)
        {
            int workerId = w;
            workers[w] = Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    for (int iter = 0; iter < 40; iter++)
                    {
                        ParticleEffect2D effect = effects[(workerId + iter) & 1];
                        using ParticleEffectInstance2D inst = system.CreateInstance(
                            effect,
                            new Transform2D(new Vector2(workerId, iter)),
                            height: 0.5f,
                            seed: workerId * 100 + iter + 1);

                        inst.Transform = new Transform2D(new Vector2(workerId + 1f, iter * 0.25f));
                        inst.Height = 1.25f;
                        inst.DepthBase = -0.1f;
                        inst.SetGroupEmissionRate(0, 20f);
                        inst.SetGroupParams(0, new EmitterParams2D { CustomData = iter });
                        _ = inst.GetGroupParams(0);
                        inst.IsVisible = (iter & 1) == 0;
                        inst.Stop();
                        inst.Play();
                        inst.Restart();
                        _ = system.Instances.Count;
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            });
        }

        Task allWorkers = Task.WhenAll(workers).ContinueWith(_ => workersDone.Set(), TaskScheduler.Default);
        startGate.Set();

        var passSig = new RenderPassSignature([Format.Rgba8Unorm]);
        Matrix4x4 vp = Matrix4x4.Identity;
        int frame = 0;
        while (!workersDone.IsSet || frame < 10)
        {
            context.BeginFrame();
            system.RecordSimulation(context, 1f / 60f);
            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
            {
                render.SetViewport(new Viewport { Width = 32, Height = 32 });
                render.SetScissor(new Scissor { Width = 32, Height = 32 });
                system.Render(context, render, in vp);
            }
            context.Submit();
            host.RenderingSystem.WaitIdle();
            if ((frame & 7) == 0)
                system.TrimUnusedCaches();
            frame++;
        }

        allWorkers.Wait();
        system.TrimUnusedCaches();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty);
            Assert.That(system.Instances, Is.Empty);
            Assert.That(system.Pool.AllocatedSlotCount, Is.Zero);
        });
    }

    /// <summary>Worker threads create/mutate/dispose 3D particle instances while the render thread simulates and renders.</summary>
    [Test]
    public void ParticleSystem3D_ConcurrentLifecycleAndRender_IsThreadSafe()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuParticleSystem3D(host.RenderingSystem, host.Shaders, particleCapacity: 2048, emitterSlots: 64)
        {
            SimulationRateLimitEnabled = false,
        };
        using Texture target = host.CreateColorTarget(32, 32);
        using var context = new RenderContext(host.RenderingSystem);

        ParticleEffect3D[] effects =
        [
            new()
            {
                Name = "c3d_a",
                Groups =
                [
                    new ParticleGroup3D
                    {
                        Name = "G0",
                        MaxParticles = 64,
                        EmissionRate = 30f,
                        Lifetime = new ParticleRange(0.2f, 0.5f),
                        ColorGradient = [new ParticleColorKey { Time = 0f, Color = ColorFloat.White }, new ParticleColorKey { Time = 1f, Color = ColorFloat.Transparent }],
                        SizeCurve = [new ParticleScalarKey { Time = 0f, Value = 1f }, new ParticleScalarKey { Time = 1f, Value = 0f }],
                    },
                ],
            },
            new()
            {
                Name = "c3d_b",
                Groups =
                [
                    new ParticleGroup3D
                    {
                        Name = "G1",
                        MaxParticles = 64,
                        EmissionRate = 45f,
                        Depth = DepthStencilState.None,
                        Lifetime = new ParticleRange(0.2f, 0.4f),
                    },
                ],
            },
        ];

        using var startGate = new ManualResetEventSlim(false);
        using var workersDone = new ManualResetEventSlim(false);
        var errors = new ConcurrentQueue<Exception>();

        Task[] workers = new Task[4];
        for (int w = 0; w < workers.Length; w++)
        {
            int workerId = w;
            workers[w] = Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    for (int iter = 0; iter < 40; iter++)
                    {
                        ParticleEffect3D effect = effects[(workerId + iter) & 1];
                        using ParticleEffectInstance3D inst = system.CreateInstance(
                            effect,
                            new Transform3D(new Vector3(workerId, iter, 0f)),
                            seed: workerId * 100 + iter + 1);

                        inst.Transform = new Transform3D(new Vector3(workerId + 1f, iter * 0.25f, 1f));
                        inst.SetGroupEmissionRate(0, 20f);
                        inst.SetGroupParams(0, new EmitterParams3D { CustomData = iter });
                        _ = inst.GetGroupParams(0);
                        inst.IsVisible = (iter & 1) == 0;
                        inst.Stop();
                        inst.Play();
                        inst.Restart();
                        _ = system.Instances.Count;
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            });
        }

        Task allWorkers = Task.WhenAll(workers).ContinueWith(_ => workersDone.Set(), TaskScheduler.Default);
        startGate.Set();

        var passSig = new RenderPassSignature([Format.Rgba8Unorm]);
        Matrix4x4 vp = Matrix4x4.Identity;
        int frame = 0;
        while (!workersDone.IsSet || frame < 10)
        {
            system.DepthStencilState = (frame & 1) == 0 ? DepthStencilState.ReadReverseZ : DepthStencilState.None;
            context.BeginFrame();
            system.RecordSimulation(context, 1f / 60f);
            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
            {
                render.SetViewport(new Viewport { Width = 32, Height = 32 });
                render.SetScissor(new Scissor { Width = 32, Height = 32 });
                system.Render(context, render, in vp, Vector3.UnitX, Vector3.UnitZ);
            }
            context.Submit();
            host.RenderingSystem.WaitIdle();
            if ((frame & 7) == 0)
                system.TrimUnusedCaches();
            frame++;
        }

        allWorkers.Wait();
        system.TrimUnusedCaches();

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty);
            Assert.That(system.Instances, Is.Empty);
            Assert.That(system.Pool.AllocatedSlotCount, Is.Zero);
        });
    }

    /// <summary>Concurrent material module registration, refresh, disposal, and instance creation stay race-free.</summary>
    [Test]
    public void ParticleSystems_ConcurrentMaterialModuleRegistrationAndDispose_IsThreadSafe()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var sys2D = new GpuParticleSystem2D(host.RenderingSystem, host.Shaders, particleCapacity: 1024, emitterSlots: 64);
        using var sys3D = new GpuParticleSystem3D(host.RenderingSystem, host.Shaders, particleCapacity: 1024, emitterSlots: 64);

        var mat = new MaterialAsset { Name = "mod_test", Surface = "TestParticleSurface" };
        var eff2D = new ParticleEffect2D { Name = "m2d", Groups = [new ParticleGroup2D { Name = "G", MaxParticles = 64, Material = mat }] };
        var eff3D = new ParticleEffect3D { Name = "m3d", Groups = [new ParticleGroup3D { Name = "G", MaxParticles = 64, Material = mat }] };

        using var gate = new ManualResetEventSlim(false);
        var errors = new ConcurrentQueue<Exception>();

        Task[] tasks = new Task[4];
        for (int t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                gate.Wait();
                try
                {
                    for (int i = 0; i < 25; i++)
                    {
                        using IDisposable r2 = sys2D.AddMaterialModule(new CountingModule());
                        using IDisposable r3 = sys3D.AddMaterialModule(new CountingModule());
                        sys2D.RefreshMaterialModules();
                        sys3D.RefreshMaterialModules();
                        using ParticleEffectInstance2D i2 = sys2D.CreateInstance(eff2D, new Transform2D(Vector2.Zero));
                        using ParticleEffectInstance3D i3 = sys3D.CreateInstance(eff3D, new Transform3D(Vector3.Zero));
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            });
        }

        gate.Set();
        Task.WaitAll(tasks);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Empty);
            Assert.That(sys2D.Pool.AllocatedSlotCount, Is.Zero);
            Assert.That(sys3D.Pool.AllocatedSlotCount, Is.Zero);
        });
    }

    /// <summary>Worker threads create/extend/finish/dispose 2D and 3D trails while the render thread prepares and renders.</summary>
    [Test]
    public void TrailSystems2DAnd3D_ConcurrentEmitAndRender_IsThreadSafe()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var trails2D = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, pointCapacity: 4096, trailSlots: 128);
        using var trails3D = new GpuTrailSystem3D(host.RenderingSystem, host.Shaders, pointCapacity: 4096, trailSlots: 128);
        using Texture target = host.CreateColorTarget(32, 32);
        using var context = new RenderContext(host.RenderingSystem);

        var eff2D = new TrailEffect2D { ExpectedPoints = 32, Spacing = 0.25f, Life = 0.2f };
        var eff3D = new TrailEffect3D { ExpectedPoints = 32, Spacing = 0.25f, Life = 0.2f };

        using var startGate = new ManualResetEventSlim(false);
        using var workersDone = new ManualResetEventSlim(false);
        var errors = new ConcurrentQueue<Exception>();

        Task[] workers = new Task[4];
        for (int w = 0; w < workers.Length; w++)
        {
            int workerId = w;
            workers[w] = Task.Run(() =>
            {
                startGate.Wait();
                try
                {
                    for (int iter = 0; iter < 50; iter++)
                    {
                        if (trails2D.TryCreateInstance(eff2D, Vector2.Zero, out TrailEffectInstance2D t2))
                        {
                            using (t2)
                            {
                                t2.ExtendTo(new Vector2(1f + workerId, 0f), 0f);
                                t2.IsVisible = (iter & 1) == 0;
                                _ = t2.IsEmitting;
                                _ = t2.IsAlive;
                                if ((iter & 1) == 0)
                                    t2.Finish(new Vector2(2f + workerId, 0f), 0f);
                                else
                                    t2.Stop();
                            }
                        }

                        if (trails3D.TryCreateInstance(eff3D, Vector3.Zero, out TrailEffectInstance3D t3))
                        {
                            using (t3)
                            {
                                t3.ExtendTo(new Vector3(1f + workerId, 0f, 0f));
                                t3.IsVisible = (iter & 1) == 0;
                                _ = t3.IsEmitting;
                                _ = t3.IsAlive;
                                if ((iter & 1) == 0)
                                    t3.Finish(new Vector3(2f + workerId, 0f, 0f));
                                else
                                    t3.Stop();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            });
        }

        Task allWorkers = Task.WhenAll(workers).ContinueWith(_ => workersDone.Set(), TaskScheduler.Default);
        startGate.Set();

        var passSig = new RenderPassSignature([Format.Rgba8Unorm]);
        Matrix4x4 vp = Matrix4x4.Identity;
        int frame = 0;
        while (!workersDone.IsSet || frame < 10)
        {
            trails2D.Update(1f / 60f);
            trails3D.Update(1f / 60f);
            context.BeginFrame();
            trails2D.PrepareRender(context);
            trails3D.PrepareRender(context);
            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (RenderContext.RenderScope render = context.BeginRenderPass(passDesc, passSig))
            {
                render.SetViewport(new Viewport { Width = 32, Height = 32 });
                render.SetScissor(new Scissor { Width = 32, Height = 32 });
                trails2D.Render(context, render, in vp);
                trails3D.Render(context, render, in vp, Vector3.UnitZ);
            }
            context.Submit();
            host.RenderingSystem.WaitIdle();
            if ((frame & 7) == 0)
            {
                trails2D.TrimUnusedCaches();
                trails3D.TrimUnusedCaches();
            }
            frame++;
        }

        allWorkers.Wait();
        trails2D.TrimUnusedCaches();
        trails3D.TrimUnusedCaches();

        Assert.That(errors, Is.Empty);
    }
}
