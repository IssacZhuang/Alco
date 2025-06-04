using System;
using System.Numerics;
using System.Text.Json;
using System.Collections.Generic;
using NUnit.Framework;
using Alco;

namespace Alco.Engine.Test;

public class TestJsonVectorConverters
{
    [Test(Description = "Test int2 JSON conversion")]
    public void TestInt2Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterInt2() }
        };

        var original = new int2(10, 20);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"int2 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":10,\"y\":20}"));

        var deserialized = JsonSerializer.Deserialize<int2>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
        });
    }

    [Test(Description = "Test int3 JSON conversion")]
    public void TestInt3Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterInt3() }
        };

        var original = new int3(10, 20, 30);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"int3 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":10,\"y\":20,\"z\":30}"));

        var deserialized = JsonSerializer.Deserialize<int3>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
        });
    }

    [Test(Description = "Test int4 JSON conversion")]
    public void TestInt4Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterInt4() }
        };

        var original = new int4(10, 20, 30, 40);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"int4 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":10,\"y\":20,\"z\":30,\"w\":40}"));

        var deserialized = JsonSerializer.Deserialize<int4>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
            Assert.That(deserialized.W, Is.EqualTo(original.W));
        });
    }

    [Test(Description = "Test uint2 JSON conversion")]
    public void TestUInt2Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterUInt2() }
        };

        var original = new uint2(100u, 200u);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"uint2 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":100,\"y\":200}"));

        var deserialized = JsonSerializer.Deserialize<uint2>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
        });
    }

    [Test(Description = "Test uint3 JSON conversion")]
    public void TestUInt3Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterUInt3() }
        };

        var original = new uint3(100u, 200u, 300u);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"uint3 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":100,\"y\":200,\"z\":300}"));

        var deserialized = JsonSerializer.Deserialize<uint3>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
        });
    }

    [Test(Description = "Test uint4 JSON conversion")]
    public void TestUInt4Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterUInt4() }
        };

        var original = new uint4(100u, 200u, 300u, 400u);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"uint4 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":100,\"y\":200,\"z\":300,\"w\":400}"));

        var deserialized = JsonSerializer.Deserialize<uint4>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
            Assert.That(deserialized.W, Is.EqualTo(original.W));
        });
    }

    [Test(Description = "Test Half2 JSON conversion")]
    public void TestHalf2Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterHalf2() }
        };

        var original = new Half2((Half)1.5f, (Half)2.5f);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"Half2 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":1.5,\"y\":2.5}"));

        var deserialized = JsonSerializer.Deserialize<Half2>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That((float)deserialized.X, Is.EqualTo((float)original.X));
            Assert.That((float)deserialized.Y, Is.EqualTo((float)original.Y));
        });
    }

    [Test(Description = "Test Half3 JSON conversion")]
    public void TestHalf3Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterHalf3() }
        };

        var original = new Half3((Half)1.5f, (Half)2.5f, (Half)3.5f);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"Half3 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":1.5,\"y\":2.5,\"z\":3.5}"));

        var deserialized = JsonSerializer.Deserialize<Half3>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That((float)deserialized.X, Is.EqualTo((float)original.X));
            Assert.That((float)deserialized.Y, Is.EqualTo((float)original.Y));
            Assert.That((float)deserialized.Z, Is.EqualTo((float)original.Z));
        });
    }

    [Test(Description = "Test Half4 JSON conversion")]
    public void TestHalf4Conversion()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterHalf4() }
        };

        var original = new Half4((Half)1.5f, (Half)2.5f, (Half)3.5f, (Half)4.5f);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"Half4 JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":1.5,\"y\":2.5,\"z\":3.5,\"w\":4.5}"));

        var deserialized = JsonSerializer.Deserialize<Half4>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That((float)deserialized.X, Is.EqualTo((float)original.X));
            Assert.That((float)deserialized.Y, Is.EqualTo((float)original.Y));
            Assert.That((float)deserialized.Z, Is.EqualTo((float)original.Z));
            Assert.That((float)deserialized.W, Is.EqualTo((float)original.W));
        });
    }

    [Test(Description = "Test negative int values")]
    public void TestNegativeIntValues()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterInt3() }
        };

        var original = new int3(-10, -20, -30);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"int3 with negative values JSON: {json}");
        Assert.That(json, Is.EqualTo("{\"x\":-10,\"y\":-20,\"z\":-30}"));

        var deserialized = JsonSerializer.Deserialize<int3>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
        });
    }

    [Test(Description = "Test large uint values")]
    public void TestLargeUIntValues()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterUInt3() }
        };

        var original = new uint3(uint.MaxValue, uint.MaxValue - 1, uint.MaxValue / 2);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"uint3 with large values JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<uint3>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That(deserialized.X, Is.EqualTo(original.X));
            Assert.That(deserialized.Y, Is.EqualTo(original.Y));
            Assert.That(deserialized.Z, Is.EqualTo(original.Z));
        });
    }

    [Test(Description = "Test Half special values")]
    public void TestHalfSpecialValues()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterHalf3() }
        };

        var original = new Half3((Half)0.0f, (Half)(-0.0f), (Half)1.0f);
        string json = JsonSerializer.Serialize(original, options);
        TestContext.WriteLine($"Half3 with special values JSON: {json}");

        var deserialized = JsonSerializer.Deserialize<Half3>(json, options);
        Assert.Multiple(() =>
        {
            Assert.That((float)deserialized.X, Is.EqualTo((float)original.X));
            Assert.That((float)deserialized.Y, Is.EqualTo((float)original.Y));
            Assert.That((float)deserialized.Z, Is.EqualTo((float)original.Z));
        });
    }

    [Test(Description = "Test int2 invalid format")]
    public void TestInt2InvalidFormat()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterInt2() }
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<int2>("\"not an object\"", options));
    }

    [Test(Description = "Test uint3 invalid format")]
    public void TestUInt3InvalidFormat()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterUInt3() }
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<uint3>("\"not an object\"", options));
    }

    [Test(Description = "Test Half4 invalid format")]
    public void TestHalf4InvalidFormat()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new JsonConverterHalf4() }
        };

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Half4>("\"not an object\"", options));
    }
}