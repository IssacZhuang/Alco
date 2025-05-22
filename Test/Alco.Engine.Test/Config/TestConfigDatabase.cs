using System;
using NUnit.Framework;
using Alco.Engine;
using Alco.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco.Engine.Test;

[TestFixture]
public class TestConfigDatabase
{
    private class TestConfig : Configable
    {
        public string Name { get; set; } = string.Empty;
    }

    private class TestConfigLoader : IConfigLoader
    {
        public string Name => "TestConfigLoader";

        public IReadOnlyList<string> FileExtensions => new List<string> { ".test" }.AsReadOnly();

        public Configable CreateConfig(string filename, ReadOnlySpan<byte> data)
        {
            string content = Encoding.UTF8.GetString(data);
            var parts = content.Split('|');
            if (parts.Length < 2)
                throw new Exception("Invalid test config format");

            return new TestConfig
            {
                Id = parts[0],
                Name = parts[1]
            };
        }
    }

    private class TestFileSource : IFileSource
    {
        private readonly Dictionary<string, byte[]> _files = new();

        public int Priority => 0;

        public IEnumerable<string> AllFileNames => _files.Keys;

        public bool IsWriteable => false;

        public void AddFile(string filename, string content)
        {
            _files[filename] = Encoding.UTF8.GetBytes(content);
        }

        public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, out string failureReason)
        {
            if (_files.TryGetValue(path, out var bytes))
            {
                data = new SafeMemoryHandle(bytes);
                failureReason = null;
                return true;
            }

            data = SafeMemoryHandle.Empty;
            failureReason = $"File not found: {path}";
            return false;
        }

        public bool TryWriteData(string path, ReadOnlySpan<byte> data, [NotNullWhen(false)] out string failureReason)
        {
            failureReason = "TestFileSource is not writable";
            return false;
        }
    }

    [Test]
    public void TestGetConfig()
    {
        // Arrange
        var configDb = new ConfigDatabase();
        var fileSource = new TestFileSource();
        var configLoader = new TestConfigLoader();

        fileSource.AddFile("test1.test", "config1|Test Config 1");
        fileSource.AddFile("test2.test", "config2|Test Config 2");

        configDb.AddFileSource(fileSource);
        configDb.RegisterConfigLoader(configLoader);

        // Act
        var config1 = configDb.GetConfig("config1", typeof(TestConfig));
        var config2 = configDb.GetConfig("config2", typeof(TestConfig));

        // Assert
        Assert.That(config1, Is.Not.Null);
        Assert.That(config1, Is.TypeOf<TestConfig>());
        Assert.That(config1.Id, Is.EqualTo("config1"));
        Assert.That(((TestConfig)config1).Name, Is.EqualTo("Test Config 1"));

        Assert.That(config2, Is.Not.Null);
        Assert.That(config2, Is.TypeOf<TestConfig>());
        Assert.That(config2.Id, Is.EqualTo("config2"));
        Assert.That(((TestConfig)config2).Name, Is.EqualTo("Test Config 2"));
    }

    [Test]
    public void TestTryGetConfig()
    {
        // Arrange
        var configDb = new ConfigDatabase();
        var fileSource = new TestFileSource();
        var configLoader = new TestConfigLoader();

        fileSource.AddFile("test1.test", "config1|Test Config 1");

        configDb.AddFileSource(fileSource);
        configDb.RegisterConfigLoader(configLoader);

        // Act & Assert
        Assert.That(configDb.TryGetConfig("config1", typeof(TestConfig), out var config), Is.True);
        Assert.That(config, Is.Not.Null);
        Assert.That(config, Is.TypeOf<TestConfig>());
        Assert.That(config.Id, Is.EqualTo("config1"));
        Assert.That(((TestConfig)config).Name, Is.EqualTo("Test Config 1"));

        Assert.That(configDb.TryGetConfig("nonexistent", typeof(TestConfig), out var nonexistentConfig), Is.False);
        Assert.That(nonexistentConfig, Is.Null);
    }

    [Test]
    public void TestAddRemoveFileSource()
    {
        // Arrange
        var configDb = new ConfigDatabase();
        var fileSource1 = new TestFileSource();
        var fileSource2 = new TestFileSource();
        var configLoader = new TestConfigLoader();

        fileSource1.AddFile("test1.test", "config1|Test Config 1");
        fileSource2.AddFile("test2.test", "config2|Test Config 2");

        configDb.RegisterConfigLoader(configLoader);

        // Act & Assert - Add first source
        configDb.AddFileSource(fileSource1);
        Assert.That(configDb.TryGetConfig("config1", typeof(TestConfig), out _), Is.True);
        Assert.That(configDb.TryGetConfig("config2", typeof(TestConfig), out _), Is.False);

        // Act & Assert - Add second source
        configDb.AddFileSource(fileSource2);
        Assert.That(configDb.TryGetConfig("config1", typeof(TestConfig), out _), Is.True);
        Assert.That(configDb.TryGetConfig("config2", typeof(TestConfig), out _), Is.True);

        // Act & Assert - Remove first source
        configDb.RemoveFileSource(fileSource1);
        Assert.That(configDb.TryGetConfig("config1", typeof(TestConfig), out _), Is.False);
        Assert.That(configDb.TryGetConfig("config2", typeof(TestConfig), out _), Is.True);
    }

    [Test]
    public void TestRegisterUnregisterConfigLoader()
    {
        // Arrange
        var configDb = new ConfigDatabase();
        var fileSource = new TestFileSource();
        var configLoader = new TestConfigLoader();

        fileSource.AddFile("test1.test", "config1|Test Config 1");

        configDb.AddFileSource(fileSource);

        // Act & Assert - Register loader
        configDb.RegisterConfigLoader(configLoader);
        Assert.That(configDb.TryGetConfig("config1", typeof(TestConfig), out _), Is.True);

        // Act & Assert - Unregister loader
        configDb.UnregisterConfigLoader(configLoader);

        // This should throw because the config is no longer loaded after the loader was unregistered
        Assert.Throws<Exception>(() => configDb.GetConfig("config1", typeof(TestConfig)));
        Assert.That(configDb.TryGetConfig("config1", typeof(TestConfig), out _), Is.False);
    }

    [Test]
    public void TestGetConfigNotFound()
    {
        // Arrange
        var configDb = new ConfigDatabase();

        // Act & Assert
        Assert.Throws<Exception>(() => configDb.GetConfig("nonexistent", typeof(TestConfig)));
    }
}