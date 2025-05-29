using System;
using System.Numerics;
using System.Text.Json;
using System.Collections.Generic;
using NUnit.Framework;
using Alco; // Add import for Color32 and ColorFloat

namespace Alco.Engine.Test;



public class TestJsonConverters
{

    private class TestObject
    {
        public Vector2 Position2D { get; set; }
        public Vector3 Position3D { get; set; }
        public Vector4 Color { get; set; }
        public Quaternion Rotation { get; set; }
    }

    private readonly JsonSerializerOptions _options;

    public TestJsonConverters()
    {
        _options = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonConverterVector2(),
                new JsonConverterVector3(),
                new JsonConverterVector4(),
                new JsonConverterQuaternion(),
                new JsonConverterColorFloat(),
                new JsonConverterColor32() // Add Color32 converter to options
            }
        };
    }

    [Test(Description = "Test Vector2 JSON conversion")]
    public void TestVector2Conversion()
    {
        var original = new Vector2(1.5f, 2.5f);
        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"Vector2 JSON: {json}");
        Assert.That(json, Is.EqualTo("[1.5,2.5]"));

        var deserialized = JsonSerializer.Deserialize<Vector2>(json, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
        });
    }

    [Test(Description = "Test Vector3 JSON conversion")]
    public void TestVector3Conversion()
    {
        var original = new Vector3(1.5f, 2.5f, 3.5f);
        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"Vector3 JSON: {json}");
        Assert.That(json, Is.EqualTo("[1.5,2.5,3.5]"));

        var deserialized = JsonSerializer.Deserialize<Vector3>(json, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
        });
    }

    [Test(Description = "Test Vector4 JSON conversion")]
    public void TestVector4Conversion()
    {
        var original = new Vector4(1.5f, 2.5f, 3.5f, 4.5f);
        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"Vector4 JSON: {json}");
        Assert.That(json, Is.EqualTo("[1.5,2.5,3.5,4.5]"));

        var deserialized = JsonSerializer.Deserialize<Vector4>(json, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
            Assert.That(deserialized.W, Is.EqualTo(original.W));
        });
    }

    [Test(Description = "Test Quaternion JSON conversion")]
    public void TestQuaternionConversion()
    {
        var original = new Quaternion(1.5f, 2.5f, 3.5f, 4.5f);
        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"Quaternion JSON: {json}");
        Assert.That(json, Is.EqualTo("[1.5,2.5,3.5,4.5]"));

        var deserialized = JsonSerializer.Deserialize<Quaternion>(json, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
            Assert.That(deserialized.W, Is.EqualTo(original.W));
        });
    }

    [Test(Description = "Test Vector2 JSON invalid format")]
    public void TestVector2InvalidFormat()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector2>("[1]", _options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector2>("[1,2,3]", _options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector2>("\"not an array\"", _options));
    }

    [Test(Description = "Test Vector3 JSON invalid format")]
    public void TestVector3InvalidFormat()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector3>("[1,2]", _options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector3>("[1,2,3,4]", _options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector3>("\"not an array\"", _options));
    }

    [Test(Description = "Test Vector4 JSON invalid format")]
    public void TestVector4InvalidFormat()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector4>("[1,2,3]", _options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector4>("[1,2,3,4,5]", _options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector4>("\"not an array\"", _options));
    }

    [Test(Description = "Test Quaternion JSON invalid format")]
    public void TestQuaternionInvalidFormat()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Quaternion>("[1,2,3]", _options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Quaternion>("[1,2,3,4,5]", _options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Quaternion>("\"not an array\"", _options));
    }

    [Test(Description = "Test List<Vector3> JSON conversion")]
    public void TestVector3ListConversion()
    {
        var original = new List<Vector3>
        {
            new Vector3(1.5f, 2.5f, 3.5f),
            new Vector3(4.5f, 5.5f, 6.5f),
            new Vector3(7.5f, 8.5f, 9.5f)
        };

        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"Vector3 List JSON: {json}");
        Assert.That(json, Is.EqualTo("[[1.5,2.5,3.5],[4.5,5.5,6.5],[7.5,8.5,9.5]]"));

        var deserialized = JsonSerializer.Deserialize<List<Vector3>>(json, _options);
        Assert.That(deserialized.Count, Is.EqualTo(original.Count));

        for (int i = 0; i < original.Count; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(deserialized[i].X, Is.EqualTo(original[i].X));
                Assert.That(deserialized[i].Y, Is.EqualTo(original[i].Y));
                Assert.That(deserialized[i].Z, Is.EqualTo(original[i].Z));
            });
        }
    }

    [Test(Description = "Test Dictionary<string, Vector3> JSON conversion")]
    public void TestVector3DictionaryConversion()
    {
        var original = new Dictionary<string, Vector3>
        {
            { "position1", new Vector3(1.5f, 2.5f, 3.5f) },
            { "position2", new Vector3(4.5f, 5.5f, 6.5f) },
            { "position3", new Vector3(7.5f, 8.5f, 9.5f) }
        };

        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"Vector3 Dictionary JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"position1\":[1.5,2.5,3.5],\"position2\":[4.5,5.5,6.5],\"position3\":[7.5,8.5,9.5]}"));

        var deserialized = JsonSerializer.Deserialize<Dictionary<string, Vector3>>(json, _options);
        Assert.That(deserialized.Count, Is.EqualTo(original.Count));

        foreach (var kvp in original)
        {
            Assert.That(deserialized.ContainsKey(kvp.Key));
            Assert.Multiple(() =>
            {
                Assert.That(deserialized[kvp.Key].X, Is.EqualTo(kvp.Value.X));
                Assert.That(deserialized[kvp.Key].Y, Is.EqualTo(kvp.Value.Y));
                Assert.That(deserialized[kvp.Key].Z, Is.EqualTo(kvp.Value.Z));
            });
        }
    }

    [Test(Description = "Test object containing Vector2/3/4 and Quaternion JSON conversion")]
    public void TestComplexObjectConversion()
    {
        var original = new TestObject
        {
            Position2D = new Vector2(1.5f, 2.5f),
            Position3D = new Vector3(3.5f, 4.5f, 5.5f),
            Color = new Vector4(0.1f, 0.2f, 0.3f, 1.0f),
            Rotation = new Quaternion(0.5f, 0.5f, 0.5f, 1.0f)
        };

        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"Complex Object JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"Position2D\":[1.5,2.5],\"Position3D\":[3.5,4.5,5.5],\"Color\":[0.1,0.2,0.3,1],\"Rotation\":[0.5,0.5,0.5,1]}"));

        var deserialized = JsonSerializer.Deserialize<TestObject>(json, _options);
        Assert.Multiple(() =>
        {
            // Check Position2D
            Assert.That(deserialized.Position2D.X, Is.EqualTo(original.Position2D.X));
            Assert.That(deserialized.Position2D.Y, Is.EqualTo(original.Position2D.Y));

            // Check Position3D
            Assert.That(deserialized.Position3D.X, Is.EqualTo(original.Position3D.X));
            Assert.That(deserialized.Position3D.Y, Is.EqualTo(original.Position3D.Y));
            Assert.That(deserialized.Position3D.Z, Is.EqualTo(original.Position3D.Z));

            // Check Color
            Assert.That(deserialized.Color.X, Is.EqualTo(original.Color.X));
            Assert.That(deserialized.Color.Y, Is.EqualTo(original.Color.Y));
            Assert.That(deserialized.Color.Z, Is.EqualTo(original.Color.Z));
            Assert.That(deserialized.Color.W, Is.EqualTo(original.Color.W));

            // Check Rotation
            Assert.That(deserialized.Rotation.X, Is.EqualTo(original.Rotation.X));
            Assert.That(deserialized.Rotation.Y, Is.EqualTo(original.Rotation.Y));
            Assert.That(deserialized.Rotation.Z, Is.EqualTo(original.Rotation.Z));
            Assert.That(deserialized.Rotation.W, Is.EqualTo(original.Rotation.W));
        });
    }

    private class TestTypeObject
    {
        public Type SystemType { get; set; }
        public Type CustomType { get; set; }
    }

    [Test(Description = "Test Type JSON conversion")]
    public void TestTypeConversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterType(typeof(TestTypeObject).Assembly) }
        };

        var original = new TestTypeObject
        {
            SystemType = typeof(int),
            CustomType = typeof(TestTypeObject)
        };

        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"Type Object JSON: {json}");
        var deserialized = JsonSerializer.Deserialize<TestTypeObject>(json, options);

        Assert.Multiple(() =>
        {
            Assert.That(deserialized.SystemType, Is.EqualTo(typeof(int)));
            Assert.That(deserialized.CustomType, Is.EqualTo(typeof(TestTypeObject)));
        });
    }

    [Test(Description = "Test Type JSON null handling")]
    public void TestTypeNullHandling()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterType() }
        };

        var original = new TestTypeObject
        {
            SystemType = null,
            CustomType = null
        };

        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"Null Type Object JSON: {json}");
        var deserialized = JsonSerializer.Deserialize<TestTypeObject>(json, options);

        Assert.Multiple(() =>
        {
            Assert.That(deserialized.SystemType, Is.Null);
            Assert.That(deserialized.CustomType, Is.Null);
        });
    }

    [Test(Description = "Test Type JSON invalid format")]
    public void TestTypeInvalidFormat()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterType() }
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Type>("123", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Type>("[1,2,3]", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Type>("\"NonExistentType\"", options));
    }

    private class TestConfig : Configable
    {
        public string Name { get; set; }
        public int Value { get; set; }

        public TestConfig()
        {
            Name = string.Empty;
            Id = string.Empty;
        }
    }


    [Test(Description = "Test JsonConverterConfig basic serialization")]
    public void TestConfigConversion()
    {
        var typeResolver = new ConfigJsonTypeResolver();
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        var original = new TestConfig
        {
            Name = "test",
            Value = 42
        };

        string json = JsonSerializer.Serialize<Configable>(original, options);
        TestContext.WriteLine($"Config JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<Configable>(json, options) as TestConfig;

        Assert.That(deserialized, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized!.Name, Is.EqualTo(original.Name));
            Assert.That(deserialized.Value, Is.EqualTo(original.Value));
        });
    }

    [Test(Description = "Test JsonConverterConfig with null value")]
    public void TestConfigNull()
    {
        var typeResolver = new ConfigJsonTypeResolver();
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        Configable original = null;
        string json = JsonSerializer.Serialize<Configable>(original, options);
        TestContext.WriteLine($"Null Config JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<Configable>(json, options);
        Assert.That(deserialized, Is.Null);
    }

    [Test(Description = "Test JsonConverterConfig with invalid type")]
    public void TestConfigInvalidType()
    {
        var typeResolver = new ConfigJsonTypeResolver();
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        string invalidJson = @"{""$type"":""NonExistentType"",""name"":""test""}";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Configable>(invalidJson, options));
    }

    [Test(Description = "Test ColorFloat JSON conversion")]
    public void TestColorFloatConversion()
    {
        var original = new ColorFloat(0.5f, 0.6f, 0.7f, 0.8f);
        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"ColorFloat JSON: {json}");
        Assert.That(json, Is.EqualTo("[0.5,0.6,0.7,0.8]"));

        var deserialized = JsonSerializer.Deserialize<ColorFloat>(json, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.R, Is.EqualTo(original.R));
            Assert.That(deserialized.G, Is.EqualTo(original.G));
            Assert.That(deserialized.B, Is.EqualTo(original.B));
            Assert.That(deserialized.A, Is.EqualTo(original.A));
        });
    }

    [Test(Description = "Test ColorFloat JSON conversion with default alpha")]
    public void TestColorFloatConversionDefaultAlpha()
    {
        var original = new ColorFloat(0.5f, 0.6f, 0.7f);
        string json = JsonSerializer.Serialize(original, _options);
        TestContext.WriteLine($"ColorFloat JSON with default alpha: {json}");
        Assert.That(json, Is.EqualTo("[0.5,0.6,0.7,1]"));

        // Test deserialization of RGB only (alpha should default to 1.0)
        var rgbJson = "[0.5,0.6,0.7]";
        var deserialized = JsonSerializer.Deserialize<ColorFloat>(rgbJson, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.R, Is.EqualTo(original.R));
            Assert.That(deserialized.G, Is.EqualTo(original.G));
            Assert.That(deserialized.B, Is.EqualTo(original.B));
            Assert.That(deserialized.A, Is.EqualTo(1.0f));
        });
    }

    [Test(Description = "Test Color32 JSON conversion")]
    public void TestColor32Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonConverterColor32()
            }
        };

        var original = new Color32(128, 64, 32, 255);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"Color32 JSON: {json}");
        Assert.That(json, Is.EqualTo("[128,64,32,255]"));

        var deserialized = JsonSerializer.Deserialize<Color32>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.R, Is.EqualTo(original.R));
            Assert.That(deserialized.G, Is.EqualTo(original.G));
            Assert.That(deserialized.B, Is.EqualTo(original.B));
            Assert.That(deserialized.A, Is.EqualTo(original.A));
        });
    }

    [Test(Description = "Test Color32 JSON conversion with default alpha")]
    public void TestColor32ConversionDefaultAlpha()
    {
        var options = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonConverterColor32()
            }
        };

        var original = new Color32(128, 64, 32);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"Color32 JSON with default alpha: {json}");
        Assert.That(json, Is.EqualTo("[128,64,32,255]"));

        // Test deserialization of RGB only (alpha should default to 255)
        var rgbJson = "[128,64,32]";
        var deserialized = JsonSerializer.Deserialize<Color32>(rgbJson, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.R, Is.EqualTo(original.R));
            Assert.That(deserialized.G, Is.EqualTo(original.G));
            Assert.That(deserialized.B, Is.EqualTo(original.B));
            Assert.That(deserialized.A, Is.EqualTo(255));
        });
    }

    [Test(Description = "Test ColorFloat JSON hex string conversion")]
    public void TestColorFloatHexStringConversion()
    {
        // Test 8-digit hex (RGBA)
        var hexJson = "\"FF8040C0\""; // Red=255, Green=128, Blue=64, Alpha=192
        var deserialized = JsonSerializer.Deserialize<ColorFloat>(hexJson, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.R, Is.EqualTo(1.0f).Within(0.01f)); // 255/255 = 1.0
            Assert.That(deserialized.G, Is.EqualTo(0.502f).Within(0.01f)); // 128/255 ≈ 0.502
            Assert.That(deserialized.B, Is.EqualTo(0.251f).Within(0.01f)); // 64/255 ≈ 0.251
            Assert.That(deserialized.A, Is.EqualTo(0.753f).Within(0.01f)); // 192/255 ≈ 0.753
        });
        TestContext.WriteLine($"ColorFloat from hex string {hexJson}: R={deserialized.R}, G={deserialized.G}, B={deserialized.B}, A={deserialized.A}");

        // Test 6-digit hex (RGB with default alpha=1.0)
        var hex6Json = "\"FF8040\""; // Red=255, Green=128, Blue=64
        var deserialized6 = JsonSerializer.Deserialize<ColorFloat>(hex6Json, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized6.R, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(deserialized6.G, Is.EqualTo(0.502f).Within(0.01f));
            Assert.That(deserialized6.B, Is.EqualTo(0.251f).Within(0.01f));
            Assert.That(deserialized6.A, Is.EqualTo(1.0f).Within(0.01f)); // Default alpha should be 1.0
        });
        TestContext.WriteLine($"ColorFloat from 6-digit hex string {hex6Json}: R={deserialized6.R}, G={deserialized6.G}, B={deserialized6.B}, A={deserialized6.A}");
    }


    [Test(Description = "Test Color32 JSON hex string conversion")]
    public void TestColor32HexStringConversion()
    {
        // Test 8-digit hex (RGBA)
        var hexJson = "\"FF8040C0\""; // Red=255, Green=128, Blue=64, Alpha=192
        var deserialized = JsonSerializer.Deserialize<Color32>(hexJson, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.R, Is.EqualTo(255));
            Assert.That(deserialized.G, Is.EqualTo(128));
            Assert.That(deserialized.B, Is.EqualTo(64));
            Assert.That(deserialized.A, Is.EqualTo(192));
        });
        TestContext.WriteLine($"Color32 from hex string {hexJson}: R={deserialized.R}, G={deserialized.G}, B={deserialized.B}, A={deserialized.A}");

        // Test 6-digit hex (RGB with default alpha=255)
        var hex6Json = "\"FF8040\""; // Red=255, Green=128, Blue=64
        var deserialized6 = JsonSerializer.Deserialize<Color32>(hex6Json, _options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized6.R, Is.EqualTo(255));
            Assert.That(deserialized6.G, Is.EqualTo(128));
            Assert.That(deserialized6.B, Is.EqualTo(64));
            Assert.That(deserialized6.A, Is.EqualTo(255)); // Default alpha should be 255
        });
        TestContext.WriteLine($"Color32 from 6-digit hex string {hex6Json}: R={deserialized6.R}, G={deserialized6.G}, B={deserialized6.B}, A={deserialized6.A}");
    }


    [Test(Description = "Test ColorFloat JSON mixed hex and array formats")]
    public void TestColorFloatMixedFormats()
    {
        // Test that both hex string and array formats work for the same color value
        var hexJson = "\"80404020\""; // Red=128, Green=64, Blue=64, Alpha=32
        var arrayJson = "[0.502,0.251,0.251,0.125]"; // Equivalent float values

        var fromHex = JsonSerializer.Deserialize<ColorFloat>(hexJson, _options);
        var fromArray = JsonSerializer.Deserialize<ColorFloat>(arrayJson, _options);

        Assert.Multiple(() =>
        {
            Assert.That(fromHex.R, Is.EqualTo(fromArray.R).Within(0.01f));
            Assert.That(fromHex.G, Is.EqualTo(fromArray.G).Within(0.01f));
            Assert.That(fromHex.B, Is.EqualTo(fromArray.B).Within(0.01f));
            Assert.That(fromHex.A, Is.EqualTo(fromArray.A).Within(0.01f));
        });
        TestContext.WriteLine($"ColorFloat from hex: R={fromHex.R}, G={fromHex.G}, B={fromHex.B}, A={fromHex.A}");
        TestContext.WriteLine($"ColorFloat from array: R={fromArray.R}, G={fromArray.G}, B={fromArray.B}, A={fromArray.A}");
    }

    [Test(Description = "Test Color32 JSON mixed hex and array formats")]
    public void TestColor32MixedFormats()
    {
        var options = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonConverterColor32()
            }
        };

        // Test that both hex string and array formats work for the same color value
        var hexJson = "\"80404020\""; // Red=128, Green=64, Blue=64, Alpha=32
        var arrayJson = "[128,64,64,32]"; // Equivalent byte values

        var fromHex = JsonSerializer.Deserialize<Color32>(hexJson, options);
        var fromArray = JsonSerializer.Deserialize<Color32>(arrayJson, options);

        Assert.Multiple(() =>
        {
            Assert.That(fromHex.R, Is.EqualTo(fromArray.R));
            Assert.That(fromHex.G, Is.EqualTo(fromArray.G));
            Assert.That(fromHex.B, Is.EqualTo(fromArray.B));
            Assert.That(fromHex.A, Is.EqualTo(fromArray.A));
        });
        TestContext.WriteLine($"Color32 from hex: R={fromHex.R}, G={fromHex.G}, B={fromHex.B}, A={fromHex.A}");
        TestContext.WriteLine($"Color32 from array: R={fromArray.R}, G={fromArray.G}, B={fromArray.B}, A={fromArray.A}");
    }

    [Test(Description = "Test ColorFloat JSON common hex color values")]
    public void TestColorFloatCommonHexColors()
    {
        // Test common colors in hex format
        var whiteJson = "\"FFFFFFFF\"";
        var blackJson = "\"000000FF\"";
        var redJson = "\"FF0000FF\"";
        var greenJson = "\"00FF00FF\"";
        var blueJson = "\"0000FFFF\"";

        var white = JsonSerializer.Deserialize<ColorFloat>(whiteJson, _options);
        var black = JsonSerializer.Deserialize<ColorFloat>(blackJson, _options);
        var red = JsonSerializer.Deserialize<ColorFloat>(redJson, _options);
        var green = JsonSerializer.Deserialize<ColorFloat>(greenJson, _options);
        var blue = JsonSerializer.Deserialize<ColorFloat>(blueJson, _options);

        Assert.Multiple(() =>
        {
            // White
            Assert.That(white.R, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(white.G, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(white.B, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(white.A, Is.EqualTo(1.0f).Within(0.01f));

            // Black  
            Assert.That(black.R, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(black.G, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(black.B, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(black.A, Is.EqualTo(1.0f).Within(0.01f));

            // Red
            Assert.That(red.R, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(red.G, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(red.B, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(red.A, Is.EqualTo(1.0f).Within(0.01f));

            // Green
            Assert.That(green.R, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(green.G, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(green.B, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(green.A, Is.EqualTo(1.0f).Within(0.01f));

            // Blue
            Assert.That(blue.R, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(blue.G, Is.EqualTo(0.0f).Within(0.01f));
            Assert.That(blue.B, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(blue.A, Is.EqualTo(1.0f).Within(0.01f));
        });
    }

    [Test(Description = "Test Color32 JSON common hex color values")]
    public void TestColor32CommonHexColors()
    {
        var options = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonConverterColor32()
            }
        };

        // Test common colors in hex format
        var whiteJson = "\"FFFFFFFF\"";
        var blackJson = "\"000000FF\"";
        var redJson = "\"FF0000FF\"";
        var greenJson = "\"00FF00FF\"";
        var blueJson = "\"0000FFFF\"";

        var white = JsonSerializer.Deserialize<Color32>(whiteJson, options);
        var black = JsonSerializer.Deserialize<Color32>(blackJson, options);
        var red = JsonSerializer.Deserialize<Color32>(redJson, options);
        var green = JsonSerializer.Deserialize<Color32>(greenJson, options);
        var blue = JsonSerializer.Deserialize<Color32>(blueJson, options);

        Assert.Multiple(() =>
        {
            // White
            Assert.That(white.R, Is.EqualTo(255));
            Assert.That(white.G, Is.EqualTo(255));
            Assert.That(white.B, Is.EqualTo(255));
            Assert.That(white.A, Is.EqualTo(255));

            // Black  
            Assert.That(black.R, Is.EqualTo(0));
            Assert.That(black.G, Is.EqualTo(0));
            Assert.That(black.B, Is.EqualTo(0));
            Assert.That(black.A, Is.EqualTo(255));

            // Red
            Assert.That(red.R, Is.EqualTo(255));
            Assert.That(red.G, Is.EqualTo(0));
            Assert.That(red.B, Is.EqualTo(0));
            Assert.That(red.A, Is.EqualTo(255));

            // Green
            Assert.That(green.R, Is.EqualTo(0));
            Assert.That(green.G, Is.EqualTo(255));
            Assert.That(green.B, Is.EqualTo(0));
            Assert.That(green.A, Is.EqualTo(255));

            // Blue
            Assert.That(blue.R, Is.EqualTo(0));
            Assert.That(blue.G, Is.EqualTo(0));
            Assert.That(blue.B, Is.EqualTo(255));
            Assert.That(blue.A, Is.EqualTo(255));
        });
    }
}