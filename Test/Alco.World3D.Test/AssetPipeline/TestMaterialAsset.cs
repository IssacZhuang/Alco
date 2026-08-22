using System.Numerics;
using System.Text;
using NUnit.Framework;
using Alco.IO;

namespace Alco.World3D.Test;

/// <summary>
/// Parsing and loading of material asset files (.amat): field mapping, defaults,
/// version/enum/vector validation and the loader round trip through the asset system.
/// </summary>
public class TestMaterialAsset
{
    private static MaterialAsset Parse(string json, string filename = "test.amat")
    {
        return MaterialAssetJson.Parse(Encoding.UTF8.GetBytes(json), filename);
    }

    [Test]
    public void ParseMapsEveryField()
    {
        const string json = """
        {
            "version": "1.0",
            "name": "wall_brick",
            "baseColorFactor": [0.5, 0.6, 0.7, 0.9],
            "metallicFactor": 0.2,
            "roughnessFactor": 0.8,
            "emissiveFactor": [0.1, 0.0, 0.2],
            "alphaMode": "Mask",
            "alphaCutoff": 0.35,
            "doubleSided": true,
            "textures": {
                "albedo": "Rungholt\\rungholt-RGB.png",
                "normal": "Rungholt/rungholt-normal.png",
                "metallicRoughness": "",
                "emissive": null
            }
        }
        """;

        MaterialAsset material = Parse(json);

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
            // Authored backslashes normalize to asset-root separators; empty and null
            // slots stay absent.
            Assert.That(material.Textures["albedo"], Is.EqualTo("Rungholt/rungholt-RGB.png"));
            Assert.That(material.Textures["normal"], Is.EqualTo("Rungholt/rungholt-normal.png"));
            Assert.That(material.Textures, Does.Not.ContainKey("metallicRoughness"));
            Assert.That(material.Textures, Does.Not.ContainKey("emissive"));
            Assert.That(material.EnumerateTexturePaths(), Is.EqualTo(new[] { "Rungholt/rungholt-RGB.png", "Rungholt/rungholt-normal.png" }));
        });
    }

    [Test]
    public void ParseMapsSurfaceShaderDefinesAndCustomSlots()
    {
        const string json = """
        {
            "version": "1.0",
            "name": "mossy_rock",
            "shader": "Shaders\\Materials\\MossyRock.slang",
            "defines": ["MOSS_ANIMATE", " MOSS_ANIMATE ", ""],
            "textures": { "noiseMap": "Textures/noise.png" }
        }
        """;

        MaterialAsset material = Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(material.SurfaceShader, Is.EqualTo("Shaders/Materials/MossyRock.slang"));
            // Defines trim to uniqueness; empty entries drop.
            Assert.That(material.Defines, Is.EqualTo(new[] { "MOSS_ANIMATE" }));
            Assert.That(material.Textures["noiseMap"], Is.EqualTo("Textures/noise.png"));
            Assert.That(material.EnumerateTexturePaths(), Is.EqualTo(new[] { "Textures/noise.png" }));
        });
    }

    [Test]
    public void ParseRejectsDefinesWithWhitespace()
    {
        Assert.That(() => Parse("""{ "version": "1.0", "defines": ["A B"] }"""),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ParseAppliesDefaultsToMinimalFile()
    {
        MaterialAsset material = Parse("""{ "version": "1.0" }""", "mat_wall.amat");

        Assert.Multiple(() =>
        {
            Assert.That(material.Name, Is.EqualTo("mat_wall"));
            Assert.That(material.SurfaceShader, Is.Null);
            Assert.That(material.Defines, Is.Empty);
            Assert.That(material.BaseColorFactor, Is.EqualTo(Vector4.One));
            Assert.That(material.MetallicFactor, Is.EqualTo(0.0f));
            Assert.That(material.RoughnessFactor, Is.EqualTo(1.0f));
            Assert.That(material.EmissiveFactor, Is.EqualTo(Vector3.Zero));
            Assert.That(material.AlphaMode, Is.EqualTo(MeshAlphaMode.Opaque));
            Assert.That(material.AlphaCutoff, Is.EqualTo(0.5f));
            Assert.That(material.DoubleSided, Is.False);
            Assert.That(material.Textures, Is.Empty);
            Assert.That(material.EnumerateTexturePaths(), Is.Empty);
        });
    }

    [Test]
    public void ParseRejectsFutureMajorVersion()
    {
        Assert.That(() => Parse("""{ "version": "2.0" }"""),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ParseRejectsMissingVersion()
    {
        Assert.That(() => Parse("""{ "name": "wall" }"""),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ParseRejectsUnknownAlphaMode()
    {
        Assert.That(() => Parse("""{ "version": "1.0", "alphaMode": "Dithered" }"""),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ParseRejectsMalformedVectors()
    {
        Assert.That(() => Parse("""{ "version": "1.0", "baseColorFactor": [1, 1, 1] }"""),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Parse("""{ "version": "1.0", "emissiveFactor": [1, 2, 3, 4] }"""),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void LoaderServesCachedInstancePerFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_amat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "wall.amat"), """
                {
                    "version": "1.0",
                    "textures": { "albedo": "wall.png" }
                }
                """);

            using TestAssetHost host = new();
            AssetSystem assets = new(host);
            assets.AddFileSource(new DirectoryFileSource(directory));
            World3DAssetPipeline.RegisterLoaders(assets);

            MaterialAsset first = assets.Load<MaterialAsset>("wall.amat");
            MaterialAsset second = assets.Load<MaterialAsset>("wall.amat");

            Assert.Multiple(() =>
            {
                Assert.That(first.Name, Is.EqualTo("wall"));
                Assert.That(first.Textures["albedo"], Is.EqualTo("wall.png"));
                Assert.That(second, Is.SameAs(first), "The asset system must cache material assets per file.");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
