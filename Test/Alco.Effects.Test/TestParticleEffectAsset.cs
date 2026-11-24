using System.Numerics;
using System.Text.Json;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Effects.Test;

/// <summary>Tests JSON parsing, asset loading, validation, and GPU parameter baking for <see cref="ParticleEffect2D"/> and <see cref="ParticleEffect3D"/>.</summary>
public class TestParticleEffectAsset
{
    /// <summary>The minimal engine host with <c>.afx</c>/<c>.afx3</c> loaders registered.</summary>
    public sealed class Host(GameEngineSetting setting) : GameEngine(setting)
    {
        /// <inheritdoc />
        public override IEnumerable<IAssetLoader> CreateDefaultAssetLoaders()
        {
            foreach (IAssetLoader loader in base.CreateDefaultAssetLoaders())
                yield return loader;
            yield return new AssetLoaderParticleEffect(AssetSystem);
        }
    }

    private Host _engine = null!;

    /// <summary>Creates the shared no-GPU engine host.</summary>
    [OneTimeSetUp]
    public void SetUp()
    {
        _engine = new Host(GameEngineSetting.CreateNoGPU());
    }

    /// <summary>Disposes the shared engine host.</summary>
    [OneTimeTearDown]
    public void TearDown()
    {
        _engine.Dispose();
    }

    /// <summary>Tests parsing a multi-group 2D effect with color gradient, size curve, material, and custom behavior.</summary>
    [Test]
    public void ParsesFullParticleEffect2DAndBakesEmitterParams()
    {
        const string json = """
        {
            "$type": "Alco.Effects.ParticleEffect2D",
            "version": "1.0",
            "groups": [
                {
                    "name": "Dissolve",
                    "maxParticles": 512,
                    "looping": false,
                    "duration": 2.5,
                    "emissionRate": 0,
                    "bursts": [ { "time": 0.0, "countMin": 32, "countMax": 32 }, { "time": 0.4, "countMin": 16, "countMax": 16 } ],
                    "shape": { "type": "circle", "radius": 3.0, "innerRadius": 0.5 },
                    "direction": { "x": 0, "y": 1 },
                    "spreadAngle": 0.6,
                    "speed": { "min": 4, "max": 8 },
                    "alignRotationToVelocity": true,
                    "velocityStretch": true,
                    "stretchSpeedScale": 0.25,
                    "stretchLengthScale": 1.5,
                    "lifetime": { "min": 0.5, "max": 1.2 },
                    "size": { "min": { "x": 1.0, "y": 1.0 }, "max": { "x": 2.0, "y": 3.0 } },
                    "startRotation": { "min": -1.0, "max": 1.0 },
                    "angularVelocity": { "min": -2.0, "max": 2.0 },
                    "gravity": { "x": 0, "y": -9.8 },
                    "drag": 0.4,
                    "colorGradient": [
                        { "time": 0.0, "color": "#FFFFFFFF" },
                        { "time": 1.0, "color": "#FF000000" }
                    ],
                    "sizeCurve": [
                        { "time": 0.0, "value": 0.2 },
                        { "time": 0.5, "value": 1.5 },
                        { "time": 1.0, "value": 0.0 }
                    ],
                    "flipbook": { "rows": 2, "cols": 4, "cycles": 2.0, "framesPerAnim": 4, "reverse": true },
                    "material": "Materials/TestParticleMat.amat",
                    "behavior": "TestBehavior2D",
                    "texture": "TestNoise",
                    "blend": "Additive"
                }
            ]
        }
        """;

        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(_engine.AssetSystem);
        var effect = JsonSerializer.Deserialize<ParticleEffect>(json, options) as ParticleEffect2D;

        Assert.That(effect, Is.Not.Null);
        Assert.That(effect!.Groups, Has.Count.EqualTo(1));

        ParticleGroup2D group = effect.Groups[0];
        Assert.Multiple(() =>
        {
            Assert.That(group.Name, Is.EqualTo("Dissolve"));
            Assert.That(group.MaxParticles, Is.EqualTo(512));
            Assert.That(group.Looping, Is.False);
            Assert.That(group.Duration, Is.EqualTo(2.5f));
            Assert.That(group.Bursts, Has.Count.EqualTo(2));
            Assert.That(group.Bursts[1].CountMax, Is.EqualTo(16));
            Assert.That(group.Shape.Type, Is.EqualTo(ParticleShape2DType.Circle));
            Assert.That(group.Shape.Radius, Is.EqualTo(3.0f));
            Assert.That(group.Shape.InnerRadius, Is.EqualTo(0.5f));
            Assert.That(group.Material, Is.Not.Null);
            Assert.That(group.Material!.Surface, Is.EqualTo("TestParticleSurface"));
            Assert.That(group.Material.Textures.ContainsKey("noiseTexture"), Is.True);
            Assert.That(group.Behavior, Is.EqualTo("TestBehavior2D"));
            Assert.That(group.Texture, Is.Not.Null);
            Assert.That(group.Blend, Is.EqualTo(BlendState.Additive));
        });

        EmitterParams2D baked = EmitterParams2D.FromAsset(group);
        Assert.Multiple(() =>
        {
            Assert.That(baked.Flags & EmitterParams2D.FlagColorGradient, Is.Not.Zero);
            Assert.That(baked.Flags & EmitterParams2D.FlagSizeCurve, Is.Not.Zero);
            Assert.That(baked.Flags & EmitterParams2D.FlagVelocityStretch, Is.Not.Zero);
            Assert.That(baked.Flags & EmitterParams2D.FlagFlipbookReverse, Is.Not.Zero);
            Assert.That(baked.WorldMatrix, Is.EqualTo(Matrix4x4.Identity));
            Assert.That(baked.Flipbook, Is.EqualTo(new Vector4(2f, 4f, 2f, 4f)));
        });
    }

    /// <summary>Tests parsing a 3D particle effect with custom behavior and baking <see cref="EmitterParams3D"/>.</summary>
    [Test]
    public void ParsesParticleEffect3DAndBakesEmitterParams()
    {
        const string json = """
        {
            "$type": "Alco.Effects.ParticleEffect3D",
            "version": "1.0",
            "groups": [
                {
                    "name": "Swirl",
                    "maxParticles": 256,
                    "shape": { "type": "hemisphere", "radius": 2.0 },
                    "direction": { "x": 0, "y": 0, "z": 1 },
                    "spreadAngle": 0.5,
                    "simulationSpace": "Local",
                    "behavior": "TestBehavior3D",
                    "material": "Materials/TestParticleMat.amat"
                }
            ]
        }
        """;

        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(_engine.AssetSystem);
        var effect = JsonSerializer.Deserialize<ParticleEffect>(json, options) as ParticleEffect3D;

        Assert.That(effect, Is.Not.Null);
        ParticleGroup3D group = effect!.Groups[0];
        Assert.Multiple(() =>
        {
            Assert.That(group.Shape.Type, Is.EqualTo(ParticleShape3DType.Hemisphere));
            Assert.That(group.SimulationSpace, Is.EqualTo(ParticleSimulationSpace.Local));
            Assert.That(group.Behavior, Is.EqualTo("TestBehavior3D"));
            Assert.That(group.Material, Is.Not.Null);
            Assert.That(group.Material!.Surface, Is.EqualTo("TestParticleSurface"));
        });

        EmitterParams3D baked = EmitterParams3D.FromAsset(group);
        Assert.Multiple(() =>
        {
            Assert.That(baked.Flags & EmitterParams3D.FlagWorldSpace, Is.Zero);
            Assert.That(baked.Shape.X, Is.EqualTo((float)ParticleShape3DType.Hemisphere));
            Assert.That(baked.Shape.Y, Is.EqualTo(2.0f));
        });
    }

    /// <summary>Tests that invalid versions, unknown shape types, and missing groups throw clear exceptions.</summary>
    [Test]
    public void InvalidEffectPayloadsFailWithDiagnosticErrors()
    {
        JsonSerializerOptions options = AssetLoaderParticleEffect.CreateJsonOptions(_engine.AssetSystem);

        const string badShape = """
        {
            "$type": "Alco.Effects.ParticleEffect2D",
            "version": "1.0",
            "groups": [ { "shape": { "type": "dodecahedron" } } ]
        }
        """;
        JsonException? shapeEx = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ParticleEffect>(badShape, options));
        Assert.That(shapeEx!.Message, Does.Contain("dodecahedron").Or.Contain("ParticleShape2D"));

        var emptyCodeEffect = new ParticleEffect2D { Name = "EmptyCode", Groups = [] };
        using (var particleSystem = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 256, emitterSlots: 8))
        {
            InvalidDataException? emptyEx = Assert.Throws<InvalidDataException>(() =>
                particleSystem.CreateInstance(emptyCodeEffect, new Transform2D(Vector2.Zero)));
            Assert.That(emptyEx!.Message, Does.Contain("no groups"));
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"AlcoEffectsInvalid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dirSource = new DirectoryFileSource(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "BadVersion.afx"),
                """{ "$type": "Alco.Effects.ParticleEffect2D", "version": "99.0", "groups": [{}] }""");
            File.WriteAllText(Path.Combine(tempDir, "EmptyGroups.afx"),
                """{ "$type": "Alco.Effects.ParticleEffect2D", "version": "1.0", "groups": [] }""");

            _engine.AssetSystem.AddFileSource(dirSource);

            AssetLoadException? versionEx = Assert.Throws<AssetLoadException>(() =>
                _engine.AssetSystem.Load<ParticleEffect2D>("BadVersion.afx"));
            Assert.That(versionEx!.ToString(), Does.Contain("99.0"));

            AssetLoadException? groupsEx = Assert.Throws<AssetLoadException>(() =>
                _engine.AssetSystem.Load<ParticleEffect2D>("EmptyGroups.afx"));
            Assert.That(groupsEx!.ToString(), Does.Contain("no groups"));
        }
        finally
        {
            _engine.AssetSystem.RemoveFileSource(dirSource);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>Tests loading an <c>.afx</c> effect file through <see cref="AssetSystem"/>.</summary>
    [Test]
    public void AssetLoaderLoadsEffectFromMemorySource()
    {
        const string afx = """
        {
            "$type": "Alco.Effects.ParticleEffect2D",
            "version": "1.0",
            "groups": [
                {
                    "name": "Burst",
                    "maxParticles": 64,
                    "emissionRate": 20,
                    "material": "Materials/TestParticleMat.amat"
                }
            ]
        }
        """;

        string tempDir = Path.Combine(Path.GetTempPath(), $"AlcoEffectsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string virtualPath = "TestLoadedEffect.afx";
        var dirSource = new DirectoryFileSource(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, virtualPath), afx);
            _engine.AssetSystem.AddFileSource(dirSource);

            ParticleEffect2D loaded = _engine.AssetSystem.Load<ParticleEffect2D>(virtualPath);
            Assert.Multiple(() =>
            {
                Assert.That(loaded.Groups, Has.Count.EqualTo(1));
                Assert.That(loaded.Groups[0].Name, Is.EqualTo("Burst"));
                Assert.That(loaded.Groups[0].Material, Is.Not.Null);
                Assert.That(loaded.Groups[0].Material!.Surface, Is.EqualTo("TestParticleSurface"));
            });
        }
        finally
        {
            _engine.AssetSystem.RemoveFileSource(dirSource);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
