using System.Text.Json;
using NUnit.Framework;
using Alco.IO;

namespace Alco.Engine.Test;

[TestFixture]
public class TestAssetLoaderConfig
{
    private class TestConfig : Configable
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public Configable Reference { get; set; }
    }

    private class TestFileSource : IFileSource
    {
        private readonly Dictionary<string, byte[]> _files = new();

        public int Priority => 0;

        public IEnumerable<string> AllFileNames => _files.Keys;

        public bool IsWriteable => false;

        public void AddFile(string path, string content)
        {
            _files[path] = System.Text.Encoding.UTF8.GetBytes(content);
        }

        public bool TryGetData(string path, out SafeMemoryHandle data, out string failedReason)
        {
            if (_files.TryGetValue(path, out var bytes))
            {
                data = new SafeMemoryHandle(bytes);
                failedReason = string.Empty;
                return true;
            }
            data = default;
            failedReason = $"File not found: {path}";
            return false;
        }

        public void Dispose()
        {
        }

        public bool TryWriteData(string path, ReadOnlySpan<byte> data, out string failureReason)
        {
            throw new NotImplementedException();
        }
    }

    private AssetSystem _assetSystem;
    private AssetLoaderConfig _configLoader;
    private TestFileSource _fileSource;

    [SetUp]
    public void Setup()
    {
        var lifeCycleProvider = new LifeCycleProvider();
        _assetSystem = new AssetSystem(lifeCycleProvider, 2);
        var configReferenceResolver = new ConfigReferenceResolver(_assetSystem);
        var jsonSerializerOptions = Configable.BuildJsonSerializerOptions(configReferenceResolver);
        _configLoader = new AssetLoaderConfig(jsonSerializerOptions, configReferenceResolver);
        _fileSource = new TestFileSource();
        _assetSystem.RegisterAssetLoader(_configLoader);
        _assetSystem.AddFileSource(_fileSource);
    }

    [Test]
    public void TestBasicLoading()
    {
        // Arrange
        string json = @"{
            ""$type"": ""Alco.Engine.Test.TestAssetLoaderConfig+TestConfig"",
            ""Name"": ""Test Config"",
            ""Value"": 42
        }";
        _fileSource.AddFile("test.json", json);

        // Act
        var config = _assetSystem.Load<TestConfig>("test.json");

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(config.Id, Is.EqualTo("test.json"));
            Assert.That(config.Name, Is.EqualTo("Test Config"));
            Assert.That(config.Value, Is.EqualTo(42));
            Assert.That(config.Reference, Is.Null);
        });
    }

    [Test]
    public void TestReferenceResolution()
    {
        // Arrange
        string referencedJson = @"{
            ""$type"": ""Alco.Engine.Test.TestAssetLoaderConfig+TestConfig"",
            ""Name"": ""Referenced Config"",
            ""Value"": 100
        }";
        string mainJson = @"{
            ""$type"": ""Alco.Engine.Test.TestAssetLoaderConfig+TestConfig"",
            ""Name"": ""Main Config"",
            ""Value"": 42,
            ""Reference"": ""config2.json""
        }";

        _fileSource.AddFile("config2.json", referencedJson);
        _fileSource.AddFile("config1.json", mainJson);

        // Act
        var config = _assetSystem.Load<TestConfig>("config1.json");

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config.Reference, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(config.Id, Is.EqualTo("config1.json"));
            Assert.That(config.Name, Is.EqualTo("Main Config"));
            Assert.That(config.Value, Is.EqualTo(42));
            Assert.That(config.Reference.Id, Is.EqualTo("config2.json"));
            Assert.That(((TestConfig)config.Reference).Name, Is.EqualTo("Referenced Config"));
            Assert.That(((TestConfig)config.Reference).Value, Is.EqualTo(100));
        });
    }

    [Test]
    public void TestCircularReference()
    {
        // Arrange
        string config1Json = @"{
            ""$type"": ""Alco.Engine.Test.TestAssetLoaderConfig+TestConfig"",
            ""Name"": ""Config 1"",
            ""Value"": 1,
            ""Reference"": ""config2.json""
        }";
        string config2Json = @"{
            ""$type"": ""Alco.Engine.Test.TestAssetLoaderConfig+TestConfig"",
            ""Name"": ""Config 2"",
            ""Value"": 2,
            ""Reference"": ""config1.json""
        }";

        _fileSource.AddFile("config1.json", config1Json);
        _fileSource.AddFile("config2.json", config2Json);

        // Act
        var config1 = _assetSystem.Load<TestConfig>("config1.json");

        // Assert
        Assert.That(config1, Is.Not.Null);
        Assert.That(config1.Reference, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(config1.Id, Is.EqualTo("config1.json"));
            Assert.That(config1.Reference.Id, Is.EqualTo("config2.json"));
            Assert.That(((TestConfig)config1.Reference).Reference.Id, Is.EqualTo("config1.json"));

            // Verify reference equality
            var config2 = (TestConfig)config1.Reference;
            Assert.That(ReferenceEquals(config2.Reference, config1), Is.True, "Circular reference should point back to the same config1 instance");
        });
    }

    [Test]
    public void TestLoadNonExistentFile()
    {
        // Act & Assert
        Assert.Throws<AssetLoadException>(() =>
        {
            _assetSystem.Load<TestConfig>("nonexistent.json");
        });
    }

    [Test]
    public void TestInvalidJson()
    {
        // Arrange
        string invalidJson = @"{
            ""$type"": ""Alco.Engine.Test.TestAssetLoaderConfig+TestConfig"",
            ""Id"": ""config1"",
            ""Name"": 42, // Wrong type for Name
            ""Value"": ""not a number"" // Wrong type for Value
        }";
        _fileSource.AddFile("invalid.json", invalidJson);

        // Act & Assert
        Assert.Throws<AssetLoadException>(() =>
        {
            _assetSystem.Load<TestConfig>("invalid.json");
        });
    }

    [Test]
    public void TestSelfReference()
    {
        // Arrange
        string selfRefJson = @"{
            ""$type"": ""Alco.Engine.Test.TestAssetLoaderConfig+TestConfig"",
            ""Name"": ""Self Reference Config"",
            ""Value"": 42,
            ""Reference"": ""self.json""
        }";

        _fileSource.AddFile("self.json", selfRefJson);

        // Act
        var config = _assetSystem.Load<TestConfig>("self.json");

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config.Reference, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(config.Id, Is.EqualTo("self.json"));
            Assert.That(config.Name, Is.EqualTo("Self Reference Config"));
            Assert.That(config.Value, Is.EqualTo(42));
            Assert.That(config.Reference.Id, Is.EqualTo("self.json"));
            Assert.That(((TestConfig)config.Reference).Reference.Id, Is.EqualTo("self.json"));

            // Verify reference equality
            Assert.That(config.Reference, Is.SameAs(config));
        });
    }

    private class LifeCycleProvider : IAssetSystemHost
    {
        event Action IAssetSystemHost.OnHandleAssetLoaded
        {
            add
            {

            }

            remove
            {

            }
        }

        event Action IAssetSystemHost.OnDispose
        {
            add
            {

            }

            remove
            {

            }
        }

        void IAssetSystemHost.LogError(ReadOnlySpan<char> message)
        {
            TestContext.WriteLine($"[Error] {message}");
        }

        void IAssetSystemHost.LogInfo(ReadOnlySpan<char> message)
        {
            TestContext.WriteLine($"[Info] {message}");
        }

        void IAssetSystemHost.LogSuccess(ReadOnlySpan<char> message)
        {
            TestContext.WriteLine($"[Success] {message}");
        }

        void IAssetSystemHost.LogWarning(ReadOnlySpan<char> message)
        {
            TestContext.WriteLine($"[Warning] {message}");
        }
    }
}