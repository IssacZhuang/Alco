#nullable enable

using System.Numerics;
using System.Text;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// The material asset file schema (<c>.amat</c>): the pipeline-agnostic base mapping,
/// the <c>type</c> discriminator dispatch to registered pipeline-family schemas, and
/// the shared validation (version, defines, parameters).
/// </summary>
[TestFixture]
public class TestMaterialAssetJson
{
    /// <summary>A pipeline-family asset for the dispatch tests.</summary>
    private sealed class TestMaterialAsset : MaterialAsset
    {
        public Vector4 Tint { get; init; } = Vector4.One;
    }

    /// <summary>The family schema: the base fields plus a tint color.</summary>
    private sealed class TestFamilyJson : MaterialAssetJson
    {
        public float[]? Tint { get; set; }

        protected override MaterialAsset Map(string filename)
        {
            Validate(filename);
            return new TestMaterialAsset
            {
                Name = MapName(filename),
                SurfaceShader = AssetJson.NormalizePath(Shader),
                Defines = MapDefines(Defines, filename),
                Textures = MapTextures(Textures),
                Parameters = MapParameters(Parameters, filename),
                Tint = Tint is { Length: 4 } tint
                    ? new Vector4(tint[0], tint[1], tint[2], tint[3])
                    : Vector4.One,
            };
        }
    }

    /// <summary>A second family schema, only for the conflicting-registration test.</summary>
    private sealed class OtherFamilyJson : MaterialAssetJson;

    private static MaterialAsset Parse(string json, string filename = "test.amat")
        => MaterialAssetJson.Parse(Encoding.UTF8.GetBytes(json), filename);

    [Test]
    public void BaseSchemaParsesWithoutATypeDiscriminator()
    {
        const string json = """
        {
            // author-friendly: comments and trailing commas are fine
            "version": "1.0",
            "name": "mossy_rock",
            "shader": "Shaders\\Materials\\MossyRock.slang",
            "defines": ["MOSS_ANIMATE", " MOSS_ANIMATE ", ""],
            "textures": { "noiseMap": "Textures\\noise.png", "unused": "" },
            "parameters": { "pulseSpeed": 1.5, "pulseColor": [1.0, 0.5, 0.25] },
        }
        """;

        MaterialAsset material = Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(material, Is.TypeOf<MaterialAsset>(),
                "A file without a type discriminator parses as the base schema.");
            Assert.That(material.Name, Is.EqualTo("mossy_rock"));
            Assert.That(material.SurfaceShader, Is.EqualTo("Shaders/Materials/MossyRock.slang"),
                "Authored backslashes normalize to asset-root separators.");
            // Defines trim to uniqueness; empty entries drop.
            Assert.That(material.Defines, Is.EqualTo(new[] { "MOSS_ANIMATE" }));
            Assert.That(material.Textures["noiseMap"], Is.EqualTo("Textures/noise.png"));
            Assert.That(material.Textures, Does.Not.ContainKey("unused"), "Empty texture slots stay absent.");
            Assert.That(material.Parameters["pulseSpeed"], Is.EqualTo(new[] { 1.5f }));
            Assert.That(material.Parameters["pulseColor"], Is.EqualTo(new[] { 1.0f, 0.5f, 0.25f }));
        });
    }

    [Test]
    public void BaseSchemaAppliesDefaultsToMinimalFile()
    {
        MaterialAsset material = Parse("""{ "version": "1.0" }""", "mat_wall.amat");

        Assert.Multiple(() =>
        {
            Assert.That(material.Name, Is.EqualTo("mat_wall"), "The name defaults to the source file name.");
            Assert.That(material.SurfaceShader, Is.Null);
            Assert.That(material.Defines, Is.Empty);
            Assert.That(material.Textures, Is.Empty);
            Assert.That(material.Parameters, Is.Empty);
        });
    }

    [Test]
    public void TypeDiscriminatorSelectsTheRegisteredSchema()
    {
        MaterialAssetJson.RegisterType<TestFamilyJson>("test");

        MaterialAsset typed = Parse("""
            { "version": "1.0", "type": "test", "name": "hero", "tint": [0.1, 0.2, 0.3, 0.4] }
            """);
        MaterialAsset plain = Parse("""{ "version": "1.0", "name": "hero" }""");

        Assert.Multiple(() =>
        {
            Assert.That(typed, Is.TypeOf<TestMaterialAsset>());
            Assert.That(((TestMaterialAsset)typed).Tint, Is.EqualTo(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
            Assert.That(typed.Name, Is.EqualTo("hero"));
            Assert.That(plain, Is.TypeOf<MaterialAsset>(),
                "The base schema stays the fallback for files without a type.");
        });
    }

    [Test]
    public void UnknownTypeFailsNamingTheRegisteredOnes()
    {
        MaterialAssetJson.RegisterType<TestFamilyJson>("test");

        Assert.That(() => Parse("""{ "version": "1.0", "type": "toon" }"""),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("toon"));
    }

    [Test]
    public void ConflictingTypeRegistrationThrows()
    {
        // Re-registering the same mapping is a no-op; a conflicting one throws.
        MaterialAssetJson.RegisterType<TestFamilyJson>("conflict_probe");
        Assert.Multiple(() =>
        {
            Assert.That(() => MaterialAssetJson.RegisterType<TestFamilyJson>("conflict_probe"), Throws.Nothing);
            Assert.That(() => MaterialAssetJson.RegisterType<OtherFamilyJson>("conflict_probe"),
                Throws.ArgumentException);
        });
    }

    [Test]
    public void VersionValidates()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Parse("""{ "name": "wall" }"""), Throws.TypeOf<InvalidDataException>(),
                "A missing version is rejected.");
            Assert.That(() => Parse("""{ "version": "2.0" }"""), Throws.TypeOf<InvalidDataException>(),
                "A future major version is rejected.");
            Assert.That(() => Parse("""{ "version": "1.7" }"""), Throws.Nothing,
                "Minor differences are forward compatible.");
        });
    }

    [Test]
    public void DefinesAndParametersValidate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Parse("""{ "version": "1.0", "defines": ["A B"] }"""),
                Throws.TypeOf<InvalidDataException>(), "Defines carry no whitespace.");
            Assert.That(() => Parse("""{ "version": "1.0", "parameters": { "p": [] } }"""),
                Throws.TypeOf<InvalidDataException>(), "Parameters take 1-4 components.");
            Assert.That(() => Parse("""{ "version": "1.0", "parameters": { "p": [1, 2, 3, 4, 5] } }"""),
                Throws.TypeOf<InvalidDataException>(), "Parameters take 1-4 components.");
            Assert.That(() => Parse("""{ "version": "1.0", "parameters": { "p": "fast" } }"""),
                Throws.TypeOf<InvalidDataException>(), "Parameters are numbers.");
            Assert.That(() => Parse("""{ "version": "1.0", "parameters": { "p": [1, "x"] } }"""),
                Throws.TypeOf<InvalidDataException>(), "Parameter components are numbers.");
        });
    }
}
