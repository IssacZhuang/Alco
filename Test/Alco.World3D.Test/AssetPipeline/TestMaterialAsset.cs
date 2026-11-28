using System.Numerics;
using System.Text;
using System.Text.Json;
using Alco.Engine;
using Alco.IO;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>
/// Covers World3D <see cref="PbrMaterialAsset"/> parsing, defaults, canonical texture slots, and conversion to <see cref="World3DMaterialDesc"/>.
/// </summary>
[Category("NoGPU")]
public class TestMaterialAsset
{
    private const string PbrType = "\"$type\": \"Alco.World3D.PbrMaterialAsset\"";

    private static MaterialAsset Parse(GameEngine engine, string json)
    {
        _ = typeof(PbrMaterialAsset);
        JsonSerializerOptions options = AssetLoaderMaterialAsset.CreateJsonOptions(engine.AssetSystem);
        return JsonSerializer.Deserialize<MaterialAsset>(Encoding.UTF8.GetBytes(json), options)!;
    }

    private static PbrMaterialAsset ParsePbr(GameEngine engine, string json)
    {
        MaterialAsset material = Parse(engine, json);
        Assert.That(material, Is.TypeOf<PbrMaterialAsset>());
        return (PbrMaterialAsset)material;
    }

    /// <summary>Tests that all PBR material fields parse and convert into <see cref="World3DMaterialDesc"/>.</summary>
    [Test]
    public void ParseMapsEveryFieldAndBuildsWorld3DMaterialDesc()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        const string json = """
        {
            "$type": "Alco.World3D.PbrMaterialAsset",
            "version": "1.0",
            "name": "wall_glass",
            "baseColorFactor": { "x": 0.5, "y": 0.6, "z": 0.7, "w": 0.9 },
            "metallicFactor": 0.2,
            "roughnessFactor": 0.8,
            "emissiveFactor": { "r": 0.1, "g": 0.0, "b": 0.2 },
            "alphaMode": "Blend",
            "alphaCutoff": 0.35,
            "doubleSided": true,
            "transmissionFactor": 0.75,
            "ior": 1.45,
            "sampler": "AnisotropicRepeat"
        }
        """;

        PbrMaterialAsset material = ParsePbr(engine, json);

        Assert.Multiple(() =>
        {
            Assert.That(PbrMaterialAsset.DefaultSurfaceModule, Is.EqualTo("World3DPbrStandard"));
            Assert.That(material.Name, Is.EqualTo("wall_glass"));
            Assert.That(material.BaseColorFactor, Is.EqualTo(new Vector4(0.5f, 0.6f, 0.7f, 0.9f)));
            Assert.That(material.MetallicFactor, Is.EqualTo(0.2f));
            Assert.That(material.RoughnessFactor, Is.EqualTo(0.8f));
            Assert.That(material.EmissiveFactor, Is.EqualTo(new Vector3(0.1f, 0.0f, 0.2f)));
            Assert.That(material.AlphaMode, Is.EqualTo(MeshAlphaMode.Blend));
            Assert.That(material.AlphaCutoff, Is.EqualTo(0.35f));
            Assert.That(material.DoubleSided, Is.True);
            Assert.That(material.TransmissionFactor, Is.EqualTo(0.75f));
            Assert.That(material.Ior, Is.EqualTo(1.45f));
            Assert.That(material.Sampler, Is.EqualTo(World3DMaterialSampler.AnisotropicRepeat));
            Assert.That(material.Surface, Is.Null);
        });

        Assert.That(material.TryBuildMaterialDesc(_ => default, out World3DMaterialDesc desc, out string? error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(desc.BaseColor, Is.EqualTo(new Vector4(0.5f, 0.6f, 0.7f, 0.9f)));
            Assert.That(desc.Metallic, Is.EqualTo(0.2f));
            Assert.That(desc.Roughness, Is.EqualTo(0.8f));
            Assert.That(desc.Emissive, Is.EqualTo(new Vector3(0.1f, 0.0f, 0.2f)));
            Assert.That(desc.AlphaMode, Is.EqualTo(World3DAlphaMode.Blend));
            Assert.That(desc.AlphaCutoff, Is.EqualTo(0.35f));
            Assert.That(desc.DoubleSided, Is.True);
            Assert.That(desc.Transmission, Is.EqualTo(0.75f));
            Assert.That(desc.Ior, Is.EqualTo(1.45f));
            Assert.That(desc.Sampler, Is.EqualTo(World3DMaterialSampler.AnisotropicRepeat));
            Assert.That(desc.SurfaceModule, Is.EqualTo(PbrMaterialAsset.DefaultSurfaceModule));
        });
    }

    /// <summary>Tests that minimal PBR files apply expected defaults.</summary>
    [Test]
    public void ParseAppliesDefaultsToMinimalFile()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        PbrMaterialAsset material = ParsePbr(engine, $$"""{ {{PbrType}}, "version": "1.0" }""");

        Assert.Multiple(() =>
        {
            Assert.That(material.Name, Is.Empty);
            Assert.That(material.Surface, Is.Null);
            Assert.That(material.BaseColorFactor, Is.EqualTo(Vector4.One));
            Assert.That(material.MetallicFactor, Is.EqualTo(0.0f));
            Assert.That(material.RoughnessFactor, Is.EqualTo(1.0f));
            Assert.That(material.EmissiveFactor, Is.EqualTo(Vector3.Zero));
            Assert.That(material.AlphaMode, Is.EqualTo(MeshAlphaMode.Opaque));
            Assert.That(material.AlphaCutoff, Is.EqualTo(0.5f));
            Assert.That(material.DoubleSided, Is.False);
            Assert.That(material.TransmissionFactor, Is.EqualTo(0.0f));
            Assert.That(material.Ior, Is.EqualTo(1.5f));
            Assert.That(material.Sampler, Is.EqualTo(World3DMaterialSampler.LinearRepeat));
            Assert.That(material.Textures, Is.Empty);
        });
    }

    /// <summary>Tests that malformed fields, unknown types, and unknown texture slots are rejected cleanly.</summary>
    [Test]
    public void ParseAndBuildRejectInvalidInputs()
    {
        using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
        Assert.Multiple(() =>
        {
            Assert.That(() => Parse(engine, """{ "$type": "Alco.World3D.ToonMaterialAsset", "version": "1.0" }"""),
                Throws.TypeOf<JsonException>());
            Assert.That(() => Parse(engine, $$"""{ {{PbrType}}, "version": "1.0", "alphaMode": "Dithered" }"""),
                Throws.TypeOf<JsonException>());
            Assert.That(() => Parse(engine, $$"""{ {{PbrType}}, "version": "1.0", "baseColorFactor": [1, 1, 1, 1] }"""),
                Throws.TypeOf<JsonException>());
            Assert.That(() => Parse(engine, $$"""{ {{PbrType}}, "version": "1.0", "shader": "unknown" }"""),
                Throws.TypeOf<JsonException>());
        });

        PbrMaterialAsset invalidSlot = new()
        {
            Name = "bad_slot",
            Textures = new Dictionary<string, Image>
            {
                ["detailAlbedo"] = new Image(engine.RenderingSystem.TextureWhite, ownsResource: false),
            },
        };
        WorldTextureHandle validHandle = new(Guid.NewGuid(), 0, 1);
        Assert.That(invalidSlot.TryBuildMaterialDesc(_ => validHandle, out _, out string? slotError), Is.False);
        Assert.That(slotError, Does.Contain("detailAlbedo"));

        PbrMaterialAsset invalidIor = new() { Name = "bad_ior", Ior = 4.2f };
        Assert.That(invalidIor.TryBuildMaterialDesc(_ => validHandle, out _, out string? iorError), Is.False);
        Assert.That(iorError, Does.Contain("IOR"));
    }

    /// <summary>Tests that canonical textures load as <see cref="Image"/> assets and map onto <see cref="World3DMaterialDesc"/>.</summary>
    [Test]
    public void LoadResolvesCanonicalTexturesAndCustomSurface()
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
                    "surface": "ParameterizedSurface",
                    "textures": {
                        "baseColor": "wall.png",
                        "normal": "textures\\detail.png",
                        "metallicRoughness": "",
                        "emissive": null
                    }
                }
                """);

            using GameEngine engine = new(GameEngineSetting.CreateNoGPU());
            AssetSystem assets = engine.AssetSystem;
            World3DAssetPipeline.RegisterLoaders(assets);
            assets.AddFileSource(new DirectoryFileSource(directory));

            MaterialAsset first = assets.Load<MaterialAsset>("wall.amat");
            MaterialAsset second = assets.Load<MaterialAsset>("wall.amat");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.TypeOf<PbrMaterialAsset>());
                Assert.That(first.Name, Is.EqualTo("wall"));
                Assert.That(first.Surface, Is.EqualTo("ParameterizedSurface"));
                Assert.That(first.Textures.Count, Is.EqualTo(2));
                Assert.That(first.Textures["baseColor"], Is.SameAs(assets.Load<Image>("wall.png")));
                Assert.That(first.Textures["normal"], Is.SameAs(assets.Load<Image>("textures/detail.png")));
                Assert.That(second, Is.SameAs(first));
            });

            PbrMaterialAsset pbr = (PbrMaterialAsset)first;
            Guid sceneId = Guid.NewGuid();
            int nextSlot = 0;
            Assert.That(pbr.TryBuildMaterialDesc(_ => new WorldTextureHandle(sceneId, ++nextSlot, 1u),
                out World3DMaterialDesc desc, out string? error), Is.True, error);
            Assert.That(desc.SurfaceModule, Is.EqualTo("ParameterizedSurface"));
            Assert.That(desc.BaseColorTexture.Slot, Is.GreaterThan(0));
            Assert.That(desc.NormalTexture.Slot, Is.GreaterThan(0));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void WriteDummyPng(string path)
    {
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
    }
}
