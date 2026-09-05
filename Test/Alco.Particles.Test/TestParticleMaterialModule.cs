using System.Collections.Concurrent;
using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Particles.Test;

/// <summary>
/// Material-module tests of the 2D/3D systems on the no-GPU backend: a module's
/// ConfigureMaterial must reach every render material — those already cached at
/// registration, those created afterwards, and all of them again on
/// RefreshMaterialModules — while an unregistered module must configure nothing
/// further. The concurrent test guards the publication/registration
/// atomicity: whichever way material publication and module registration
/// interleave, no material may escape configuration.
/// </summary>
public class TestParticleMaterialModule
{
    /// <summary>The minimal engine host (real shader compilation on the no-GPU backend).</summary>
    public class Host(GameEngineSetting setting) : GameEngine(setting);

    private Host _engine = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _engine = new Host(GameEngineSetting.CreateNoGPU());
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _engine.Dispose();
    }

    /// <summary>A module recording the materials it configured.</summary>
    private sealed class RecordingModule : IParticleMaterialModule
    {
        public List<GraphicsMaterial> Materials { get; } = [];

        public void ConfigureMaterial(GraphicsMaterial material)
        {
            Materials.Add(material);
        }
    }

    [Test(Description = "2D: registration sweeps cached materials; new materials configure at creation; refresh re-applies to all")]
    public void ModuleReachesCachedAndNewMaterials2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 256, emitterSlots: 8);
        using ParticleEffectInstance2D cached = system.CreateInstance(BuildEffect2D("cached"), new Transform2D(Vector2.Zero));

        var module = new RecordingModule();
        using IDisposable registration = system.AddMaterialModule(module);
        Assert.That(module.Materials, Has.Count.EqualTo(1), "registration missed the already-cached material");

        using ParticleEffectInstance2D fresh = system.CreateInstance(BuildEffect2D("fresh"), new Transform2D(Vector2.Zero));
        Assert.That(module.Materials, Has.Count.EqualTo(2), "the material created after registration was not configured");

        system.RefreshMaterialModules();
        Assert.Multiple(() =>
        {
            Assert.That(module.Materials, Has.Count.EqualTo(4), "refresh re-applied to something other than every cached material");
            Assert.That(module.Materials.Distinct().Count(), Is.EqualTo(2), "refresh strayed beyond the two cached materials");
        });
    }

    [Test(Description = "3D: registration sweeps cached materials; new materials configure at creation; refresh re-applies to all")]
    public void ModuleReachesCachedAndNewMaterials3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, particleCapacity: 256, emitterSlots: 8);
        using ParticleEffectInstance3D cached = system.CreateInstance(BuildEffect3D("cached"), new Transform3D(Vector3.Zero));

        var module = new RecordingModule();
        using IDisposable registration = system.AddMaterialModule(module);
        Assert.That(module.Materials, Has.Count.EqualTo(1), "registration missed the already-cached material");

        using ParticleEffectInstance3D fresh = system.CreateInstance(BuildEffect3D("fresh"), new Transform3D(Vector3.Zero));
        Assert.That(module.Materials, Has.Count.EqualTo(2), "the material created after registration was not configured");

        system.RefreshMaterialModules();
        Assert.Multiple(() =>
        {
            Assert.That(module.Materials, Has.Count.EqualTo(4), "refresh re-applied to something other than every cached material");
            Assert.That(module.Materials.Distinct().Count(), Is.EqualTo(2), "refresh strayed beyond the two cached materials");
        });
    }

    [Test(Description = "Disposing the registration unregisters the module")]
    public void DisposedRegistrationStopsConfiguring()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 256, emitterSlots: 8);
        var module = new RecordingModule();
        system.AddMaterialModule(module).Dispose();

        using ParticleEffectInstance2D instance = system.CreateInstance(BuildEffect2D("post-disposal"), new Transform2D(Vector2.Zero));
        system.RefreshMaterialModules();
        Assert.That(module.Materials, Is.Empty, "an unregistered module still configured materials");
    }

    [Test(Description = "2D: registration racing creation — a post-join sweep must still reach every material")]
    public void ConcurrentRegistrationSweepReachesEveryMaterial2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 16384, emitterSlots: 128);
        // Distinct group assets per creation: every first use publishes a new
        // material, so the post-join sweep count is deterministic however the
        // race lands (materials stay cached for the system's lifetime).
        const int creations = 96;
        var exceptions = new ConcurrentQueue<Exception>();
        var churnModule = new RecordingModule();
        var start = new Barrier(2);
        Task registrar = Task.Run(() =>
        {
            start.SignalAndWait();
            try
            {
                // Register/unregister churn concurrent with material publication.
                for (int i = 0; i < 32; i++)
                {
                    using IDisposable registration = system.AddMaterialModule(churnModule);
                    Thread.Sleep(1);
                }
            }
            catch (Exception e)
            {
                exceptions.Enqueue(e);
            }
        });
        Task creator = Task.Run(() =>
        {
            start.SignalAndWait();
            try
            {
                for (int i = 0; i < creations; i++)
                {
                    system.CreateInstance(BuildEffect2D($"race-{i}"), new Transform2D(Vector2.Zero)).Dispose();
                }
            }
            catch (Exception e)
            {
                exceptions.Enqueue(e);
            }
        });
        Task.WaitAll(registrar, creator);

        // The post-join sweep is the race oracle: publication-time configuration
        // and the registration sweep together must have covered every material.
        var sweeper = new RecordingModule();
        using IDisposable sweepRegistration = system.AddMaterialModule(sweeper);
        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty, "the concurrent registration/creation race threw");
            Assert.That(sweeper.Materials, Has.Count.EqualTo(creations),
                "a material escaped both the publication-time configuration and the registration sweep");
        });
    }

    private static ParticleEffect2D BuildEffect2D(string name)
        => new()
        {
            Name = name,
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = $"{name}-group",
                    MaxParticles = 128,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(1f, 2f),
                },
            ],
        };

    private static ParticleEffect3D BuildEffect3D(string name)
        => new()
        {
            Name = name,
            Groups =
            [
                new ParticleGroup3D
                {
                    Name = $"{name}-group",
                    MaxParticles = 128,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(1f, 2f),
                },
            ],
        };
}
