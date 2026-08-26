using System.Numerics;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>
/// Parsing and loading of World3D material asset files (.amat): the <c>$type</c> CLR-name
/// discriminator selects the derived asset type (no registration — assembly scan), resource
/// references resolve typed at load (textures load through the asset system, the surface
/// resolves into a validated <see cref="ShaderLibrary"/>), parameter values read as
/// typed <see cref="ShaderValue"/>s — numbers, integers, booleans, component objects,
/// hex colors or arrays — and the loader round trips through the asset system.
/// Uses a NoGPU engine (textures are 1x1 dummies there; the PbrStandard surface module
/// resolves from the module's shipped assets).
/// </summary>
public class TestMaterialAsset
{
    private const string PbrType = "\"$type\": \"Alco.World3D.PbrMaterialAsset\"";

    private static MaterialAsset Parse(GameEngine engine, string json)
    {
        JsonSerializerOptions options =
            AssetLoaderMaterialAsset.CreateJsonOptions(engine.AssetSystem, engine.RenderingSystem.ShaderSystem);
        return JsonSerializer.Deserialize<MaterialAsset>(Encoding.UTF8.GetBytes(json), options)!;
    }

    private static PbrMaterialAsset ParsePbr(GameEngine engine, string json)
    {
        MaterialAsset material = Parse(engine, json);
        Assert.That(material, Is.TypeOf<PbrMaterialAsset>());
        return (PbrMaterialAsset)material;
    }

    [Test]
    public void ParseMapsEveryField()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        const string json = """
        {
            "$type": "Alco.World3D.PbrMaterialAsset",
            "version": "1.0",
            "name": "wall_brick",
            "baseColorFactor": { "x": 0.5, "y": 0.6, "z": 0.7, "w": 0.9 },
            "metallicFactor": 0.2,
            "roughnessFactor": 0.8,
            "emissiveFactor": { "r": 0.1, "g": 0.0, "b": 0.2 },
            "alphaMode": "Mask",
            "alphaCutoff": 0.35,
            "doubleSided": true
        }
        """;

        PbrMaterialAsset material = ParsePbr(engine, json);

        Assert.Multiple(() =>
        {
            Assert.That(material.Name, Is.EqualTo("wall_brick"));
            Assert.That(material.BaseColorFactor, Is.EqualTo(new Vector4(0.5f, 0.6f, 0.7f, 0.9f)));
            Assert.That(material.MetallicFactor, Is.EqualTo(0.2f));
            Assert.That(material.RoughnessFactor, Is.EqualTo(0.8f));
            Assert.That(material.EmissiveFactor, Is.EqualTo(new Vector3(0.1f, 0.0f, 0.2f)));
            Assert.That(material.AlphaMode, Is.EqualTo(MeshAlphaMode.Mask));
            Assert.That(material.AlphaCutoff, Is.EqualTo(0.35f));
            Assert.That(material.DoubleSided, Is.True);
            Assert.That(material.Surface, Is.Null, "No surface named: the compiler's default composes.");
            Assert.That(material.Textures, Is.Empty);
        });
    }

    [Test]
    public void ParseMapsSurfaceAndParameterShapes()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        const string json = """
        {
            "version": "1.0",
            "name": "mossy_rock",
            "surface": "PbrStandard",
            "parameters": {
                "tint": { "r": 1, "g": 0.5, "b": 0.25, "a": 1 },
                "speed": 2,
                "glow": "#FF8040"
            }
        }
        """;

        // No $type discriminator: the pipeline-agnostic base type parses.
        MaterialAsset material = Parse(engine, json);

        Assert.Multiple(() =>
        {
            Assert.That(material, Is.TypeOf<MaterialAsset>());
            // The surface resolves (and validates) into the shared library reference.
            Assert.That(material.Surface,
                Is.SameAs(engine.RenderingSystem.ShaderSystem.GetLibrary("PbrStandard")));
            // Parameters are typed ShaderValues: component objects read
            // rgba/xyzw (missing components zero), an integer reads as int, a
            // hex color reads as authored float4.
            Assert.That(material.Parameters["tint"].GetFloats().ToArray(),
                Is.EqualTo(new[] { 1f, 0.5f, 0.25f, 1f }));
            Assert.That(material.Parameters["speed"].Kind, Is.EqualTo(ShaderValueKind.Int32));
            Assert.That(material.Parameters["speed"].GetInt(), Is.EqualTo(2));
            Assert.That(material.Parameters["glow"].GetFloats().ToArray(),
                Is.EqualTo(new[] { 1f, 128f / 255f, 64f / 255f, 1f }).Within(0.0001f));
        });
    }

    [Test]
    public void ParseReadsTypedParameterKinds()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        const string json = """
        {
            "version": "1.0",
            "parameters": {
                "level": 3,
                "enabled": true,
                "weights": [1, 2, 3, 4]
            }
        }
        """;

        MaterialAsset material = Parse(engine, json);

        Assert.Multiple(() =>
        {
            // A JSON integer without a fraction reads as int (not a broadcast float).
            Assert.That(material.Parameters["level"].Kind, Is.EqualTo(ShaderValueKind.Int32));
            Assert.That(material.Parameters["level"].GetInt(), Is.EqualTo(3));
            Assert.That(material.Parameters["enabled"].Kind, Is.EqualTo(ShaderValueKind.Bool32));
            Assert.That(material.Parameters["enabled"].GetInt(), Is.EqualTo(1));
            // An array reads as one float per element.
            Assert.That(material.Parameters["weights"].Kind, Is.EqualTo(ShaderValueKind.Float32));
            Assert.That(material.Parameters["weights"].ElementCount, Is.EqualTo(4));
            Assert.That(material.Parameters["weights"].GetFloats(3)[0], Is.EqualTo(4f));
        });
    }

    [Test]
    public void ParseAppliesDefaultsToMinimalFile()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        PbrMaterialAsset material = ParsePbr(engine, $$"""{ {{PbrType}}, "version": "1.0" }""");

        Assert.Multiple(() =>
        {
            Assert.That(material.Name, Is.Empty, "The loader backfills the file name, not the parser.");
            Assert.That(material.Surface, Is.Null);
            Assert.That(material.BaseColorFactor, Is.EqualTo(Vector4.One));
            Assert.That(material.MetallicFactor, Is.EqualTo(0.0f));
            Assert.That(material.RoughnessFactor, Is.EqualTo(1.0f));
            Assert.That(material.EmissiveFactor, Is.EqualTo(Vector3.Zero));
            Assert.That(material.AlphaMode, Is.EqualTo(MeshAlphaMode.Opaque));
            Assert.That(material.AlphaCutoff, Is.EqualTo(0.5f));
            Assert.That(material.DoubleSided, Is.False);
            Assert.That(material.Textures, Is.Empty);
        });
    }

    [Test]
    public void ParseRejectsUnknownDiscriminator()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        Assert.That(() => Parse(engine,
                """{ "$type": "Alco.World3D.ToonMaterialAsset", "version": "1.0" }"""),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void ParseRejectsUnknownAlphaMode()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        Assert.That(() => Parse(engine,
                $$"""{ {{PbrType}}, "version": "1.0", "alphaMode": "Dithered" }"""),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void ParseRejectsMalformedVectors()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        Assert.Multiple(() =>
        {
            // Arrays are not a vector shape; components are named, not positional.
            Assert.That(() => Parse(engine,
                    $$"""{ {{PbrType}}, "version": "1.0", "baseColorFactor": [1, 1, 1, 1] }"""),
                Throws.TypeOf<JsonException>());
            // Unknown component names fail.
            Assert.That(() => Parse(engine,
                    $$"""{ {{PbrType}}, "version": "1.0", "emissiveFactor": { "q": 1 } }"""),
                Throws.TypeOf<JsonException>());
            // Unparseable color strings fail.
            Assert.That(() => Parse(engine,
                    $$"""{ {{PbrType}}, "version": "1.0", "baseColorFactor": "red" }"""),
                Throws.TypeOf<JsonException>());
        });
    }

    [Test]
    public void ParseRejectsUnknownFields()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        // Strict mapping: a renamed/removed field is an authoring error,
        // not silently ignored.
        Assert.That(() => Parse(engine,
                $$"""{ {{PbrType}}, "version": "1.0", "shader": "mossy_rock" }"""),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void LoadResolvesTexturesTypedAndCaches()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_amat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "textures"));
        try
        {
            WriteDummyPng(Path.Combine(directory, "wall.png"));
            WriteDummyPng(Path.Combine(directory, "textures", "detail.png"));
            File.WriteAllText(Path.Combine(directory, "wall.amat"), """
                {
                    "$type": "Alco.World3D.PbrMaterialAsset",
                    "version": "1.0",
                    "textures": {
                        "albedo": "wall.png",
                        "normal": "textures\\detail.png",
                        "metallicRoughness": "",
                        "emissive": null
                    }
                }
                """);

            using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
            AssetSystem assets = engine.AssetSystem;
            assets.AddFileSource(new DirectoryFileSource(directory));

            MaterialAsset first = assets.Load<MaterialAsset>("wall.amat");
            MaterialAsset second = assets.Load<MaterialAsset>("wall.amat");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.TypeOf<PbrMaterialAsset>());
                Assert.That(first.Name, Is.EqualTo("wall"), "The file name backfills an unnamed material.");
                // Textures land typed, loaded through the asset system; authored
                // backslashes normalize to asset-root separators, and empty/null
                // slots stay absent.
                Assert.That(first.Textures.Count, Is.EqualTo(2));
                Assert.That(first.Textures["albedo"], Is.SameAs(assets.Load<Texture2D>("wall.png")));
                Assert.That(first.Textures["normal"], Is.SameAs(assets.Load<Texture2D>("textures/detail.png")));
                Assert.That(second, Is.SameAs(first), "The asset system must cache material assets per file.");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadResolvesSurfaceByModuleName()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_amat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "pulse.amat"), """
                {
                    "$type": "Alco.World3D.PbrMaterialAsset",
                    "version": "1.0",
                    "name": "pulse",
                    "surface": "ParameterizedSurface"
                }
                """);

            using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
            AssetSystem assets = engine.AssetSystem;
            assets.AddFileSource(new DirectoryFileSource(directory));

            MaterialAsset material = assets.Load<MaterialAsset>("pulse.amat");

            // The module name — not a file path — addresses the surface; the
            // reference resolves to the shared library (the test surface flows
            // from this fixture's own assets).
            Assert.That(material.Surface,
                Is.SameAs(engine.RenderingSystem.ShaderSystem.GetLibrary("ParameterizedSurface")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadFailsForUnknownSurfaceModule()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_amat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "ghost.amat"), """
                {
                    "$type": "Alco.World3D.PbrMaterialAsset",
                    "version": "1.0",
                    "surface": "no_such_module"
                }
                """);

            using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
            engine.AssetSystem.AddFileSource(new DirectoryFileSource(directory));

            Assert.That(() => engine.AssetSystem.Load<MaterialAsset>("ghost.amat"),
                Throws.TypeOf<AssetLoadException>(), "A typoed module name fails at load, not at first compile.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadFailsWhenTextureIsMissing()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_amat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "broken.amat"), """
                {
                    "$type": "Alco.World3D.PbrMaterialAsset",
                    "version": "1.0",
                    "textures": { "albedo": "absent.png" }
                }
                """);

            using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
            engine.AssetSystem.AddFileSource(new DirectoryFileSource(directory));

            Assert.That(() => engine.AssetSystem.Load<MaterialAsset>("broken.amat"),
                Throws.TypeOf<AssetLoadException>(),
                "A missing texture fails at load instead of silently binding the fallback.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadRejectsFutureMajorVersion()
    {
        AssertLoadFails("""{ "version": "2.0" }""");
    }

    [Test]
    public void LoadRejectsMissingVersion()
    {
        AssertLoadFails($$"""{ {{PbrType}}, "name": "wall" }""");
    }

    /// <summary>Loads a one-file material asset from a temp directory, expecting failure.</summary>
    private static void AssertLoadFails(string json)
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_amat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "bad.amat"), json);
            using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
            engine.AssetSystem.AddFileSource(new DirectoryFileSource(directory));
            Assert.That(() => engine.AssetSystem.Load<MaterialAsset>("bad.amat"),
                Throws.TypeOf<AssetLoadException>());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Writes a minimal valid PNG (a 1x1 pixel); the NoGPU loader never decodes it.</summary>
    private static void WriteDummyPng(string path)
    {
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
    }
}
