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

    private class TestConfigReferenceResolver : IConfigReferenceResolver
    {
        private readonly Dictionary<string, Configable> _configs = new();

        public void AddConfig(Configable config)
        {
            _configs[config.Id] = config;
        }

        public bool TryResolve(string id, string propertyName, Type propertyType, out Configable config)
        {
            return _configs.TryGetValue(id, out config);
        }
    }

    [Test]
    public void TestBasicReference()
    {
        // Arrange
        var resolver = new TestConfigReferenceResolver();
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
        resolver.AddConfig(referencedConfig);

        var mainConfig = new TestConfig
        {
            Id = "main",
            Name = "Main Config",
            Reference = referencedConfig
        };

        // Act
        string json = JsonSerializer.Serialize(mainConfig, options);
        var deserializedConfig = JsonSerializer.Deserialize<TestConfig>(json, options);

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
        var resolver = new TestConfigReferenceResolver();
        var typeResolver = new ConfigJsonTypeResolver(resolver);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver
        };

        // Act & Assert
        var json = @"{""Id"":""main"",""Name"":""Main Config"",""Reference"":""nonexistent""}";
        Assert.That(() => JsonSerializer.Deserialize<TestConfig>(json, options),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void TestCircularReference()
    {
        // Arrange
        var resolver = new TestConfigReferenceResolver();
        var typeResolver = new ConfigJsonTypeResolver(resolver);

        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = typeResolver,
        };

        var config1 = new TestConfig { Id = "config1", Name = "Config 1" };
        var config2 = new TestConfig { Id = "config2", Name = "Config 2" };

        config1.Reference = config2;
        config2.Reference = config1;

        resolver.AddConfig(config1);
        resolver.AddConfig(config2);

        // Act
        string json = JsonSerializer.Serialize(config1, options);
        TestContext.WriteLine(json);
        var deserializedConfig = JsonSerializer.Deserialize<TestConfig>(json, options);

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
        var resolver = new TestConfigReferenceResolver();
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
}