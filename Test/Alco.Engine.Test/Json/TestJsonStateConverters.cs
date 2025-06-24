using System;
using System.Text.Json;
using NUnit.Framework;
using Alco.Graphics;

namespace Alco.Engine.Test;

/// <summary>
/// Tests for JSON converters of graphics state types.
/// </summary>
public class TestJsonStateConverters
{
    [Test(Description = "Test DepthStencilState JSON conversion with all presets")]
    public void TestDepthStencilStateAllPresets()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterDepthStencilState() }
        };

        // Test all presets
        var presets = new (DepthStencilState state, string name)[]
        {
            (DepthStencilState.None, "None"),
            (DepthStencilState.Write, "Write"),
            (DepthStencilState.Read, "Read"),
            (DepthStencilState.Default, "Default")
        };

        foreach (var (state, name) in presets)
        {
            // Test serialization
            string json = JsonSerializer.Serialize(state, options);
            TestContext.WriteLine($"DepthStencilState.{name} JSON: {json}");
            Assert.That(json, Is.EqualTo($"\"{name}\""));

            // Test deserialization
            var deserialized = JsonSerializer.Deserialize<DepthStencilState>(json, options);
            Assert.That(deserialized, Is.EqualTo(state), $"Failed to deserialize {name}");
        }
    }

    [Test(Description = "Test DepthStencilState JSON case insensitive conversion")]
    public void TestDepthStencilStateCaseInsensitive()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterDepthStencilState() }
        };

        // Test case insensitive deserialization
        var testCases = new string[] { "none", "WRITE", "ReAd", "DEFAULT" };
        var expected = new DepthStencilState[] 
        { 
            DepthStencilState.None, 
            DepthStencilState.Write, 
            DepthStencilState.Read, 
            DepthStencilState.Default 
        };

        for (int i = 0; i < testCases.Length; i++)
        {
            string json = $"\"{testCases[i]}\"";
            var deserialized = JsonSerializer.Deserialize<DepthStencilState>(json, options);
            Assert.That(deserialized, Is.EqualTo(expected[i]), $"Failed case insensitive test for {testCases[i]}");
        }
    }

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

    [Test(Description = "Test BlendState JSON conversion with all presets")]
    public void TestBlendStateAllPresets()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterBlendState() }
        };

        // Test all presets
        var presets = new (BlendState state, string name)[]
        {
            (BlendState.Opaque, "Opaque"),
            (BlendState.AlphaBlend, "AlphaBlend"),
            (BlendState.Additive, "Additive"),
            (BlendState.PremultipliedAlpha, "PremultipliedAlpha"),
            (BlendState.NonPremultipliedAlpha, "NonPremultipliedAlpha"),
            (BlendState.Multiply, "Multiply")
        };

        foreach (var (state, name) in presets)
        {
            // Test serialization
            string json = JsonSerializer.Serialize(state, options);
            TestContext.WriteLine($"BlendState.{name} JSON: {json}");
            Assert.That(json, Is.EqualTo($"\"{name}\""));

            // Test deserialization
            var deserialized = JsonSerializer.Deserialize<BlendState>(json, options);
            Assert.That(deserialized, Is.EqualTo(state), $"Failed to deserialize {name}");
        }
    }

    [Test(Description = "Test BlendState JSON case insensitive conversion")]
    public void TestBlendStateCaseInsensitive()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterBlendState() }
        };

        // Test case insensitive deserialization
        var testCases = new string[] { "opaque", "ALPHABLEND", "AdDiTiVe" };
        var expected = new BlendState[] 
        { 
            BlendState.Opaque, 
            BlendState.AlphaBlend, 
            BlendState.Additive
        };

        for (int i = 0; i < testCases.Length; i++)
        {
            string json = $"\"{testCases[i]}\"";
            var deserialized = JsonSerializer.Deserialize<BlendState>(json, options);
            Assert.That(deserialized, Is.EqualTo(expected[i]), $"Failed case insensitive test for {testCases[i]}");
        }
    }

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