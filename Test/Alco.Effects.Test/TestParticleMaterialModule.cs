using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects.Test;

/// <summary>Verifies <see cref="IParticleMaterialModule"/> hooks, custom surface material application, and bindless cross-texture batching.</summary>
public class TestParticleMaterialModule
{
    private sealed class RecordingModule(string parameterName, float value) : IParticleMaterialModule
    {
        public int Calls { get; private set; }

        public void ConfigureMaterial(Material material)
        {
            Calls++;
            float v = value;
            if (material.Root.Find(parameterName) != null)
                material.SetData(parameterName, ref v);
        }
    }

    /// <summary>Registering and refreshing an <see cref="IParticleMaterialModule"/> configures materials safely and unregisters cleanly.</summary>
    [Test]
    public void MaterialModuleConfiguresExistingAndFutureMaterials()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuParticleSystem2D(host.RenderingSystem, host.Shaders, particleCapacity: 1024, emitterSlots: 16);

        var mat = new MaterialAsset
        {
            Name = "dissolve",
            Surface = "TestParticleSurface",
            Parameters = new Dictionary<string, ShaderValue>
            {
                ["edgeWidth"] = 0.25f,
                ["noiseScale"] = 2.0f,
                ["edgeColor"] = new Vector3(1f, 0.5f, 0f),
            },
        };
        var effect = new ParticleEffect2D
        {
            Name = "surface-effect",
            Groups = [new ParticleGroup2D { Name = "G", MaxParticles = 64, Material = mat }],
        };

        using ParticleEffectInstance2D first = system.CreateInstance(effect, new Transform2D(Vector2.Zero));

        var module = new RecordingModule("Material.edgeWidth", 0.75f);
        IDisposable handle = system.AddMaterialModule(module);
        Assert.That(module.Calls, Is.EqualTo(1));

        system.RefreshMaterialModules();
        Assert.That(module.Calls, Is.EqualTo(2));

        // Disposing the registration prevents future material compiles from invoking it.
        handle.Dispose();
        var defaultEffect = new ParticleEffect2D
        {
            Name = "default-effect",
            Groups = [new ParticleGroup2D { Name = "D", MaxParticles = 64, Blend = BlendState.Additive }],
        };
        using ParticleEffectInstance2D second = system.CreateInstance(defaultEffect, new Transform2D(Vector2.Zero));
        Assert.That(module.Calls, Is.EqualTo(2));
    }

    /// <summary>Two emitter groups with distinct sprite textures share one material and merge into a single bindless draw batch.</summary>
    [Test]
    public void BindlessSpritesWithDistinctImagesCoalesceIntoOneBatch2DAnd3D()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using Image red = host.CreateSolidImage(255, 0, 0, 255);
        using Image green = host.CreateSolidImage(0, 255, 0, 255);

        using var sys2D = new GpuParticleSystem2D(host.RenderingSystem, host.Shaders, particleCapacity: 1024, emitterSlots: 16);
        var eff2D = new ParticleEffect2D
        {
            Name = "bindless2d",
            Groups =
            [
                new ParticleGroup2D { Name = "Red", MaxParticles = 64, EmissionRate = 60f, Texture = red },
                new ParticleGroup2D { Name = "Green", MaxParticles = 64, EmissionRate = 60f, Texture = green },
            ],
        };
        using ParticleEffectInstance2D inst2D = sys2D.CreateInstance(eff2D, new Transform2D(Vector2.Zero));

        using var sys3D = new GpuParticleSystem3D(host.RenderingSystem, host.Shaders, particleCapacity: 1024, emitterSlots: 16);
        var eff3D = new ParticleEffect3D
        {
            Name = "bindless3d",
            Groups =
            [
                new ParticleGroup3D { Name = "Red", MaxParticles = 64, EmissionRate = 60f, Texture = red },
                new ParticleGroup3D { Name = "Green", MaxParticles = 64, EmissionRate = 60f, Texture = green },
            ],
        };
        using ParticleEffectInstance3D inst3D = sys3D.CreateInstance(eff3D, new Transform3D(Vector3.Zero));

        Assert.That(inst2D.GetGroupParams(0).SpriteTextureId, Is.Not.EqualTo(inst2D.GetGroupParams(1).SpriteTextureId));
        Assert.That(inst3D.GetGroupParams(0).SpriteTextureId, Is.Not.EqualTo(inst3D.GetGroupParams(1).SpriteTextureId));

        var context = new RenderContext(host.RenderingSystem);
        try
        {
            context.BeginFrame();
            sys2D.RecordSimulation(context, 1f / 60f);
            sys3D.RecordSimulation(context, 1f / 60f);
            context.DiscardFrame();

            Assert.That(sys2D.PlannedDrawBatchCount, Is.EqualTo(1));
            Assert.That(sys3D.PlannedDrawBatchCount, Is.EqualTo(1));
        }
        finally
        {
            context.Dispose();
        }
    }
}
