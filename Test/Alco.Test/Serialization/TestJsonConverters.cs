using System;
using System.Numerics;
using System.Text.Json;

namespace Alco.Test;

public class TestJsonConverters
{
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
}