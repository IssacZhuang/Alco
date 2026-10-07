using System.Text.Json;
using Alco.Editor;
using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Editor.Test;

/// <summary>
/// Tests the on-disk <c>.meta</c> format the texture document writes: PascalCase,
/// null fields omitted (so directory-cascade inheritance keeps working), enum values
/// as strings.
/// </summary>
[TestFixture]
public sealed class TestTextureMeta
{
    [Test]
    public void Save_OmitsNullFields()
    {
        string json = JsonSerializer.Serialize(new Texture2DMeta(), TextureDocument.MetaJsonOptions);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("FilterMode"));
            Assert.That(json, Does.Not.Contain("AddressMode"));
            Assert.That(json, Does.Not.Contain("SlicePadding"));
            Assert.That(json, Does.Not.Contain("PremultiplyAlpha"));
        });
    }

    [Test]
    public void Save_WritesPascalCaseEnumAsString()
    {
        var meta = new Texture2DMeta { FilterMode = FilterMode.Nearest };

        string json = JsonSerializer.Serialize(meta, TextureDocument.MetaJsonOptions);

        Assert.That(json, Does.Contain("\"FilterMode\": \"Nearest\""));
    }

    [Test]
    public void RoundTrip_PreservesAllFields()
    {
        var meta = new Texture2DMeta
        {
            FilterMode = FilterMode.Linear,
            AddressMode = AddressMode.ClampToEdge,
            PremultiplyAlpha = true,
            SlicePadding = new Padding(1, 2, 3, 4),
            Sprites = { ["head"] = new Texture2DMeta.Rect { X = 1, Y = 2, Width = 16, Height = 32 } },
        };

        string json = JsonSerializer.Serialize(meta, TextureDocument.MetaJsonOptions);
        Texture2DMeta loaded = JsonSerializer.Deserialize<Texture2DMeta>(json, TextureDocument.MetaJsonOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(loaded.FilterMode, Is.EqualTo(FilterMode.Linear));
            Assert.That(loaded.AddressMode, Is.EqualTo(AddressMode.ClampToEdge));
            Assert.That(loaded.PremultiplyAlpha, Is.True);
            Assert.That(loaded.SlicePadding!.Value.Left, Is.EqualTo(1f));
            Assert.That(loaded.SlicePadding.Value.Top, Is.EqualTo(2f));
            Assert.That(loaded.SlicePadding.Value.Right, Is.EqualTo(3f));
            Assert.That(loaded.SlicePadding.Value.Bottom, Is.EqualTo(4f));
            Assert.That(loaded.Sprites["head"].Width, Is.EqualTo(16));
            Assert.That(loaded.Sprites["head"].Height, Is.EqualTo(32));
        });
    }

    [Test]
    public void Load_ReadsLegacyPascalCaseFiles()
    {
        // As written by hand before the editor existed (see Sandbox assets).
        const string legacy = """
            {
                "FilterMode": "Nearest",
            }
            """;

        Texture2DMeta meta = JsonSerializer.Deserialize<Texture2DMeta>(legacy, TextureDocument.MetaJsonOptions)!;

        Assert.That(meta.FilterMode, Is.EqualTo(FilterMode.Nearest));
    }
}
