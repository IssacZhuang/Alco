using System;
using System.Numerics;
using System.Text.Json;
using System.Collections.Generic;
using NUnit.Framework;

namespace Alco.IO.Test;



public class TestJsonConverters
{
    private EmptyConfigReferenceResolver _emptyConfigReferenceResolver = new();

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
                new JsonConverterQuaternion()
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

    private class TestConfig : BaseConfig
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
        var typeResolver = new ConfigJsonTypeResolver(_emptyConfigReferenceResolver);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        var original = new TestConfig
        {
            Name = "test",
            Value = 42
        };

        string json = JsonSerializer.Serialize<BaseConfig>(original, options);
        TestContext.WriteLine($"Config JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<BaseConfig>(json, options) as TestConfig;

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
        var typeResolver = new ConfigJsonTypeResolver(_emptyConfigReferenceResolver);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        BaseConfig original = null;
        string json = JsonSerializer.Serialize<BaseConfig>(original, options);
        TestContext.WriteLine($"Null Config JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<BaseConfig>(json, options);
        Assert.That(deserialized, Is.Null);
    }

    [Test(Description = "Test JsonConverterConfig with invalid type")]
    public void TestConfigInvalidType()
    {
        var typeResolver = new ConfigJsonTypeResolver(_emptyConfigReferenceResolver);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        string invalidJson = @"{""$type"":""NonExistentType"",""name"":""test""}";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<BaseConfig>(invalidJson, options));
    }
}