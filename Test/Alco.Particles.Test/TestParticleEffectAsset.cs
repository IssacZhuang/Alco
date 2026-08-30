using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;
using Alco.Engine;
using Alco.Particles;

namespace Alco.Particles.Test;

/// <summary>
/// Particle effect asset (<c>.apeff</c>) parsing tests and GPU-layout guards of
/// the C#/slang struct twins.
/// </summary>
public class TestParticleEffectAsset
{
    /// <summary>The minimal engine host for the asset/shader systems.</summary>
    public class EngineHost(GameEngineSetting setting) : GameEngine(setting);

    private const string Effect2DJson = """
        {
            // a comment, tolerated
            "$type": "Alco.Particles.ParticleEffect2DAsset",
            "version": "1.0",
            "groups": [
                {
                    "name": "Sparks",
                    "maxParticles": 2048,
                    "emissionRate": 500,
                    "looping": false,
                    "duration": 2.0,
                    "bursts": [ { "time": 0.0, "countMin": 40, "countMax": 60 } ],
                    "shape": { "type": "circle", "radius": 12.0, "innerRadius": 0.5 },
                    "direction": { "x": 0, "y": 1 },
                    "directionMode": "radial",
                    "spreadAngle": 0.5,
                    "speed": { "min": 100, "max": 200 },
                    "lifetime": { "min": 0.5, "max": 1.0 },
                    "size": { "min": { "x": 2, "y": 2 }, "max": 4 },
                    "startRotation": 0.0,
                    "angularVelocity": { "min": -3, "max": 3 },
                    "alignRotationToVelocity": true,
                    "gravity": { "x": 0, "y": -300 },
                    "drag": 1.5,
                    "startColor": { "min": "#FFCC00FF", "max": "#FF5000FF" },
                    "endColor": "#00000000",
                    "fadeIn": 0.05,
                    "fadeOut": 0.4,
                    "endScale": 0.1,
                    "simulationSpace": "world",
                    "blend": "Additive",
                    "tint": { "x": 1, "y": 1, "z": 1, "w": 1 }
                },
                {
                    "name": "Smoke",
                    "maxParticles": 512
                }
            ]
        }
        """;

    [Test]
    public void ParsesEffect2DWithDefaultsAndStrictMembers()
    {
        using EngineHost engine = new(GameEngineSetting.CreateNoGPU());
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(
            engine.AssetSystem, engine.RenderingSystem.ShaderSystem);

        ParticleEffectAsset asset = JsonSerializer.Deserialize<ParticleEffectAsset>(Effect2DJson, options)
            ?? throw new InvalidDataException("empty");

        Assert.Multiple(() =>
        {
            ParticleEffect2DAsset effect = asset as ParticleEffect2DAsset
                ?? throw new AssertionException("not a 2D effect");
            Assert.That(effect.Groups, Has.Count.EqualTo(2));

            ParticleGroup2DAsset sparks = effect.Groups[0];
            Assert.That(sparks.Name, Is.EqualTo("Sparks"));
            Assert.That(sparks.MaxParticles, Is.EqualTo(2048));
            Assert.That(sparks.EmissionRate, Is.EqualTo(500f));
            Assert.That(sparks.Looping, Is.False);
            Assert.That(sparks.Duration, Is.EqualTo(2f));
            Assert.That(sparks.Bursts, Has.Count.EqualTo(1));
            Assert.That(sparks.Bursts[0].CountMax, Is.EqualTo(60));
            Assert.That(sparks.Shape.Type, Is.EqualTo(ParticleShape2DType.Circle));
            Assert.That(sparks.Shape.InnerRadius, Is.EqualTo(0.5f));
            Assert.That(sparks.DirectionMode, Is.EqualTo(ParticleDirectionMode.Radial));
            Assert.That(sparks.Speed.Min, Is.EqualTo(100f));
            Assert.That(sparks.AlignRotationToVelocity, Is.True);
            Assert.That(sparks.Gravity.Y, Is.EqualTo(-300f));
            Assert.That(sparks.StartColor.Max.R, Is.EqualTo(1f));
            Assert.That(sparks.StartColor.Max.G, Is.EqualTo(0x50 / 255f).Within(1e-6));
            Assert.That(sparks.EndColor.A, Is.EqualTo(0f));
            Assert.That(sparks.SimulationSpace, Is.EqualTo(ParticleSimulationSpace.World));
            Assert.That(sparks.Blend, Is.Not.Null);
            Assert.That(sparks.Material, Is.Null);
            Assert.That(sparks.Behavior, Is.Null);

            // The second group takes every default.
            ParticleGroup2DAsset smoke = effect.Groups[1];
            Assert.That(smoke.Shape.Type, Is.EqualTo(ParticleShape2DType.Point));
            Assert.That(smoke.SimulationSpace, Is.EqualTo(ParticleSimulationSpace.World));
            Assert.That(smoke.Tint, Is.EqualTo(ColorFloat.White));
        });

        // Strict members: an unknown field fails the parse.
        string bad = Effect2DJson.Replace("\"emissionRate\"", "\"emissionRateTypo\"");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParticleEffectAsset>(bad, options));
    }

    [Test]
    public void GroupMaterialLoadsAnAmatAssetWithSharedResources()
    {
        using EngineHost engine = new(GameEngineSetting.CreateNoGPU());
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(
            engine.AssetSystem, engine.RenderingSystem.ShaderSystem);

        // The group's material is an .amat reference (surface module + shared
        // textures + uniform parameters); the group derives its own texture over
        // the surface's "texture" slot.
        const string json = """
            {
                "$type": "Alco.Particles.ParticleEffect2DAsset",
                "version": "1.0",
                "groups": [
                    {
                        "name": "Dissolve",
                        "material": "Materials/TestParticleMat.amat",
                        "texture": "TestNoise",
                        "blend": "Additive",
                        "flipbook": { "rows": 2, "cols": 2, "fps": 12 }
                    }
                ]
            }
            """;

        ParticleEffectAsset asset = JsonSerializer.Deserialize<ParticleEffectAsset>(json, options)
            ?? throw new InvalidDataException("empty");
        ParticleEffect2DAsset effect = asset as ParticleEffect2DAsset
            ?? throw new AssertionException("not a 2D effect");
        ParticleGroup2DAsset group = effect.Groups[0];

        Assert.Multiple(() =>
        {
            Assert.That(group.Material, Is.Not.Null);
            Assert.That(group.Material!.Surface, Is.Not.Null);
            Assert.That(group.Material.Surface!.Name, Is.EqualTo("TestParticleSurface"));
            Assert.That(group.Material.Textures.ContainsKey("noiseTexture"), Is.True);
            Assert.That(group.Material.Parameters.ContainsKey("edgeWidth"), Is.True);
            Assert.That(group.Material.Parameters.ContainsKey("edgeColor"), Is.True);
            Assert.That(group.Texture, Is.Not.Null);
            Assert.That(group.Blend, Is.Not.Null);
            Assert.That(group.Flipbook, Is.Not.Null);
            Assert.That(group.Flipbook!.Cols, Is.EqualTo(2));
        });
    }

    [Test]
    public void GpuTwinsKeepTheirExpectedLayout()
    {
        // The slang twins (AlcoParticles_Core{2D,3D}.slang) document the same
        // sizes; a mismatch here means a struct drifted out of sync.
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<GpuParticle2D>(), Is.EqualTo(80));
            Assert.That(Marshal.SizeOf<GpuParticle3D>(), Is.EqualTo(80));
            Assert.That(Marshal.SizeOf<EmitterParams2D>(), Is.EqualTo(336));
            Assert.That(Marshal.SizeOf<EmitterParams3D>(), Is.EqualTo(320));
        });
    }
}
