using System.Text.Json;
using NUnit.Framework;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine.Test;

/// <summary>
/// Tests for JSON converters of graphics state types.
/// </summary>
public class TestJsonStateConverters
{
    private static readonly DepthStencilState DepthNone = new() { DepthTest = false, DepthWrite = false, DepthCompare = CompareOp.Never };
    private static readonly DepthStencilState DepthWrite = new() { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.LessEqual };
    private static readonly DepthStencilState DepthRead = new() { DepthTest = true, DepthWrite = false, DepthCompare = CompareOp.LessEqual };
    private static readonly DepthStencilState DepthWriteReverseZ = new() { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.GreaterEqual };
    private static readonly DepthStencilState DepthReadReverseZ = new() { DepthTest = true, DepthWrite = false, DepthCompare = CompareOp.GreaterEqual };
    private static readonly DepthStencilState DepthDefault = new() { DepthTest = false, DepthWrite = false, DepthCompare = CompareOp.Always };

    private static readonly BlendState BlendOpaque = new()
    {
        Enabled = false,
        Color = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
    };

    private static readonly BlendState BlendAlphaBlend = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    private static readonly BlendState BlendAdditive = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.One, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.One, Operation = BlendOp.Add },
    };

    private static readonly BlendState BlendPremultipliedAlpha = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    private static readonly BlendState BlendNonPremultipliedAlpha = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
    };

    private static readonly BlendState BlendMultiply = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.DestinationColor, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.Zero, Operation = BlendOp.Add },
    };

    private static readonly BlendState BlendAlphaBlendNoAccumulation = new()
    {
        Enabled = true,
        Color = new BlendComponentState { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha, Operation = BlendOp.Add },
        Alpha = new BlendComponentState { Source = BlendFactor.One, Destination = BlendFactor.One, Operation = BlendOp.Maximum },
    };

    /// <summary>
    /// Tests depth-stencil state JSON round-tripping for all presets.
    /// </summary>
    [Test(Description = "Test DepthStencilState JSON conversion with all presets")]
    public void TestDepthStencilStateAllPresets()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterDepthStencilState() }
        };

        var presets = new (DepthStencilState state, string name)[]
        {
            (DepthNone, "None"),
            (DepthWrite, "Write"),
            (DepthRead, "Read"),
            (DepthWriteReverseZ, "WriteReverseZ"),
            (DepthReadReverseZ, "ReadReverseZ"),
            (DepthDefault, "Default")
        };

        foreach (var (state, name) in presets)
        {
            string json = JsonSerializer.Serialize(state, options);
            TestContext.WriteLine($"DepthStencilState.{name} JSON: {json}");
            Assert.That(json, Is.EqualTo($"\"{name}\""));

            var deserialized = JsonSerializer.Deserialize<DepthStencilState>(json, options);
            Assert.That(deserialized, Is.EqualTo(state), $"Failed to deserialize {name}");
        }
    }

    /// <summary>
    /// Tests case-insensitive depth-stencil state deserialization.
    /// </summary>
    [Test(Description = "Test DepthStencilState JSON case insensitive conversion")]
    public void TestDepthStencilStateCaseInsensitive()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterDepthStencilState() }
        };

        string[] testCases = ["none", "WRITE", "ReAd", "DEFAULT"];
        DepthStencilState[] expected = [DepthNone, DepthWrite, DepthRead, DepthDefault];

        for (int i = 0; i < testCases.Length; i++)
        {
            string json = $"\"{testCases[i]}\"";
            var deserialized = JsonSerializer.Deserialize<DepthStencilState>(json, options);
            Assert.That(deserialized, Is.EqualTo(expected[i]), $"Failed case insensitive test for {testCases[i]}");
        }
    }

    /// <summary>
    /// Tests that an invalid depth-stencil state preset throws.
    /// </summary>
    [Test(Description = "Test DepthStencilState JSON invalid preset")]
    public void TestDepthStencilStateInvalidPreset()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterDepthStencilState() }
        };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DepthStencilState>("\"InvalidPreset\"", options));
    }

    /// <summary>
    /// Tests that non-string depth-stencil state values throw.
    /// </summary>
    [Test(Description = "Test DepthStencilState JSON invalid format")]
    public void TestDepthStencilStateInvalidFormat()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterDepthStencilState() }
        };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DepthStencilState>("123", options));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DepthStencilState>("{\"test\": \"value\"}", options));
    }

    /// <summary>
    /// Tests blend state JSON round-tripping for all presets.
    /// </summary>
    [Test(Description = "Test BlendState JSON conversion with all presets")]
    public void TestBlendStateAllPresets()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterBlendState() }
        };

        var presets = new (BlendState state, string name)[]
        {
            (BlendOpaque, "Opaque"),
            (BlendAlphaBlend, "AlphaBlend"),
            (BlendAdditive, "Additive"),
            (BlendPremultipliedAlpha, "PremultipliedAlpha"),
            (BlendNonPremultipliedAlpha, "NonPremultipliedAlpha"),
            (BlendMultiply, "Multiply"),
            (BlendAlphaBlendNoAccumulation, "AlphaBlendNoAccumulation")
        };

        foreach (var (state, name) in presets)
        {
            string json = JsonSerializer.Serialize(state, options);
            TestContext.WriteLine($"BlendState.{name} JSON: {json}");
            Assert.That(json, Is.EqualTo($"\"{name}\""));

            var deserialized = JsonSerializer.Deserialize<BlendState>(json, options);
            Assert.That(deserialized, Is.EqualTo(state), $"Failed to deserialize {name}");
        }
    }

    /// <summary>
    /// Tests case-insensitive blend state deserialization.
    /// </summary>
    [Test(Description = "Test BlendState JSON case insensitive conversion")]
    public void TestBlendStateCaseInsensitive()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterBlendState() }
        };

        string[] testCases = ["opaque", "ALPHABLEND", "AdDiTiVe"];
        BlendState[] expected = [BlendOpaque, BlendAlphaBlend, BlendAdditive];

        for (int i = 0; i < testCases.Length; i++)
        {
            string json = $"\"{testCases[i]}\"";
            var deserialized = JsonSerializer.Deserialize<BlendState>(json, options);
            Assert.That(deserialized, Is.EqualTo(expected[i]), $"Failed case insensitive test for {testCases[i]}");
        }
    }

    /// <summary>
    /// Tests that an invalid blend state preset throws.
    /// </summary>
    [Test(Description = "Test BlendState JSON invalid preset")]
    public void TestBlendStateInvalidPreset()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterBlendState() }
        };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<BlendState>("\"InvalidPreset\"", options));
    }

    /// <summary>
    /// Tests that non-string blend state values throw.
    /// </summary>
    [Test(Description = "Test BlendState JSON invalid format")]
    public void TestBlendStateInvalidFormat()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterBlendState() }
        };

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<BlendState>("123", options));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<BlendState>("{\"test\": \"value\"}", options));
    }
}
