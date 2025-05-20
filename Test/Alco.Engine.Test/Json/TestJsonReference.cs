using System;
using System.Text.Json;
using System.Collections.Generic;
using NUnit.Framework;

namespace Alco.Engine.Test;

[TestFixture]
public class TestJsonReference
{
    private class TestConfig : Configable
    {
        public string Name { get; set; } = string.Empty;
        public Configable Reference { get; set; }
    }


    [Test]
    public void TestBasicReference()
    {
        // Arrange

        Dictionary<string, Configable> configs = new();
        var resolver = new ConfigReferenceResolver((id, type) => configs[id]);
        var typeResolver = new ConfigJsonTypeResolver(resolver);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        var referencedConfig = new TestConfig
        {
            Id = "config1",
            Name = "Referenced Config"
        };
        configs["config1"] = referencedConfig;

        var mainConfig = new TestConfig
        {
            Id = "main",
            Name = "Main Config",
            Reference = referencedConfig
        };

        // Act
        string json = JsonSerializer.Serialize(mainConfig, options);
        var deserializedConfig = JsonSerializer.Deserialize<TestConfig>(json, options);
        resolver.ResolveReferenceFor(deserializedConfig);

        // Assert
        Assert.That(deserializedConfig, Is.Not.Null);
        Assert.That(deserializedConfig.Id, Is.EqualTo("main"));
        Assert.That(deserializedConfig.Name, Is.EqualTo("Main Config"));
        Assert.That(deserializedConfig.Reference, Is.Not.Null);
        Assert.That(deserializedConfig.Reference.Id, Is.EqualTo("config1"));
        Assert.That(deserializedConfig.Reference, Is.TypeOf<TestConfig>());
        Assert.That(((TestConfig)deserializedConfig.Reference).Name, Is.EqualTo("Referenced Config"));
    }

    [Test]
    public void TestReferenceNotFound()
    {
        // Arrange
        var resolver = new ConfigReferenceResolver((id, type) => throw new JsonException($"The config with id {id} is not found."));
        var typeResolver = new ConfigJsonTypeResolver(resolver);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        // Act & Assert
        var json = @"{""Id"":""main"",""Name"":""Main Config"",""Reference"":""nonexistent""}";
        var obj = JsonSerializer.Deserialize<TestConfig>(json, options);
        Assert.That(() => resolver.ResolveReferenceFor(obj),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void TestCircularReference()
    {
        // Arrange
        Dictionary<string, Configable> configs = new();
        var resolver = new ConfigReferenceResolver((id, type) => configs[id]);
        var typeResolver = new ConfigJsonTypeResolver(resolver);

        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver,
        };

        var config1 = new TestConfig { Id = "config1", Name = "Config 1" };
        var config2 = new TestConfig { Id = "config2", Name = "Config 2" };

        config1.Reference = config2;
        config2.Reference = config1;

        configs["config1"] = config1;
        configs["config2"] = config2;

        // Act
        string json = JsonSerializer.Serialize(config1, options);
        TestContext.WriteLine(json);
        var deserializedConfig = JsonSerializer.Deserialize<TestConfig>(json, options);
        resolver.ResolveReferenceFor(deserializedConfig);

        // Assert
        Assert.That(deserializedConfig, Is.Not.Null);
        Assert.That(deserializedConfig.Reference, Is.Not.Null);
        Assert.That(((TestConfig)deserializedConfig.Reference).Reference, Is.Not.Null);
        Assert.That(deserializedConfig.Id, Is.EqualTo("config1"));
        Assert.That(deserializedConfig.Reference.Id, Is.EqualTo("config2"));
        Assert.That(((TestConfig)deserializedConfig.Reference).Reference!.Id, Is.EqualTo("config1"));
    }

    [Test]
    public void TestNullReference()
    {
        // Arrange
        Dictionary<string, Configable> configs = new();
        var resolver = new ConfigReferenceResolver((id, type) => configs[id]);
        var typeResolver = new ConfigJsonTypeResolver(resolver);

        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        var config = new TestConfig
        {
            Id = "main",
            Name = "Main Config",
            Reference = null
        };

        // Act
        string json = JsonSerializer.Serialize(config, options);
        var deserializedConfig = JsonSerializer.Deserialize<TestConfig>(json, options);

        // Assert
        Assert.That(deserializedConfig, Is.Not.Null);
        Assert.That(deserializedConfig.Id, Is.EqualTo("main"));
        Assert.That(deserializedConfig.Name, Is.EqualTo("Main Config"));
        Assert.That(deserializedConfig.Reference, Is.Null);
    }

    [Test]
    public void TestSubResource()
    {
        Dictionary<string, Configable> configs = new();
        var resolver = new ConfigReferenceResolver((id, type) => configs[id]);
        var typeResolver = new ConfigJsonTypeResolver(resolver);

        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver,
        };

        var config = new TestConfig
        {
            Id = "main",
            Name = "Main Config",
            Reference = new TestConfig(){
                Id = "sub",
                Name = "Sub Config",
                IsSubResource = true
            }
        };

        // only id will be serialized but content will be ignored
        string json = JsonSerializer.Serialize(config, options);
        TestContext.WriteLine(json);
        var deserializedConfig = JsonSerializer.Deserialize<TestConfig>(json, options);

        TestConfig subConfig = (TestConfig)deserializedConfig.Reference;

        // Assert
        Assert.That(deserializedConfig, Is.Not.Null);
        Assert.That(deserializedConfig.Id, Is.EqualTo("main"));
        Assert.Multiple(() =>
        {
            Assert.That(deserializedConfig.Name, Is.EqualTo("Main Config"));
            Assert.That(deserializedConfig.Reference, Is.Not.Null);
        });
        Assert.Multiple(() =>
        {
            Assert.That(subConfig.Id, Is.EqualTo("sub"));
            Assert.That(subConfig.Name, Is.EqualTo("Sub Config"));
            Assert.That(subConfig.IsSubResource, Is.True);
        });
    }
}