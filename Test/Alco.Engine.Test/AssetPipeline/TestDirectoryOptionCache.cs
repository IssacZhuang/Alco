using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Alco.IO;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>
/// Unit tests for <see cref="DirectoryOptionCache{TOption}"/>,
/// covering cascade merging, caching, invalidation, and graceful error handling.
/// </summary>
[TestFixture]
public class TestDirectoryOptionCache
{
    /// <summary>
    /// Minimal <see cref="IAssetSystemHost"/> that satisfies the constructor contract
    /// and fires <see cref="OnDispose"/> when disposed.
    /// </summary>
    private class LifeCycleProvider : IAssetSystemHost, IDisposable
    {
        public event Action OnDispose;
        public void Dispose() { OnDispose?.Invoke(); }
        public void LogError(ReadOnlySpan<char> message) { }
        public void LogInfo(ReadOnlySpan<char> message) { }
        public void LogSuccess(ReadOnlySpan<char> message) { }
        public void LogWarning(ReadOnlySpan<char> message) { }
        void IAssetSystemHost.PostToMainThread(Action action) { }
    }

    /// <summary>
    /// In-memory <see cref="IFileSource"/> for injecting test files.
    /// </summary>
    private class TestFileSource : IFileSource
    {
        public string Name => "Test";
        public int Priority => 0;
        public IEnumerable<string> AllFileNames => _files.Keys;

        private readonly Dictionary<string, byte[]> _files = new();

        public void AddFile(string filename, string content)
            => _files[filename] = Encoding.UTF8.GetBytes(content);
        public void RemoveFile(string filename) => _files.Remove(filename);
        public void Clear() => _files.Clear();

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

        public bool TryGetStream(string path, [NotNullWhen(true)] out Stream stream, [NotNullWhen(false)] out string failureReason)
        {
            if (_files.TryGetValue(path, out var bytes))
            {
                stream = new MemoryStream(bytes);
                failureReason = null;
                return true;
            }
            stream = null;
            failureReason = $"File not found: {path}";
            return false;
        }

        public void Dispose() => _files.Clear();
    }

    /// <summary>
    /// Simple option model with two nullable fields for testing cascade merge logic.
    /// </summary>
    private class TestOption
    {
        public string? Name { get; set; }
        public int? Value { get; set; }
    }

    /// <summary>
    /// Concrete subclass of <see cref="DirectoryOptionCache{TOption}"/>
    /// that merges by inheriting parent values for null child fields.
    /// </summary>
    private class TestOptionCache : DirectoryOptionCache<TestOption>
    {
        protected override string OptionFileName => ".test-option.meta";
        protected override TestOption MergeOptions(TestOption parent, TestOption child)
            => new() { Name = child.Name ?? parent.Name, Value = child.Value ?? parent.Value };
        protected override JsonSerializerOptions CreateJsonOptions()
            => new() { AllowTrailingCommas = true };
        public TestOptionCache(AssetSystem assetSystem) : base(assetSystem) { }
    }

    private LifeCycleProvider _lifeCycle;
    private AssetSystem _assetSystem;
    private TestFileSource _fileSource;

    [SetUp]
    public void SetUp()
    {
        _lifeCycle = new LifeCycleProvider();
        _assetSystem = new AssetSystem(_lifeCycle);
        _fileSource = new TestFileSource();
        _assetSystem.AddFileSource(_fileSource);
    }

    [TearDown]
    public void TearDown()
    {
        _lifeCycle?.Dispose();
        _fileSource?.Dispose();
    }

    /// <summary>
    /// Verifies that <see cref="DirectoryOptionCache{TOption}.TryGetOption"/> returns false
    /// when no option files exist in the file source.
    /// </summary>
    [Test]
    public void NoOptionFiles_TryGetOptionReturnsFalse()
    {
        var cache = new TestOptionCache(_assetSystem);

        bool result = cache.TryGetOption("Textures/Weapons", out var option);

        Assert.IsFalse(result, "Should return false when no option files exist.");
        Assert.IsNull(option, "Option should be null when not found.");
    }

    /// <summary>
    /// Verifies that a single option file in a directory is returned directly
    /// without any merging.
    /// </summary>
    [Test]
    public void SingleOption_ReturnedDirectly()
    {
        _fileSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Name\":\"Sword\",\"Value\":42}");

        var cache = new TestOptionCache(_assetSystem);

        bool result = cache.TryGetOption("Textures/Weapons", out var option);

        Assert.IsTrue(result, "Should return true when an option file exists.");
        Assert.AreEqual("Sword", option.Name);
        Assert.AreEqual(42, option.Value);
    }

    /// <summary>
    /// Verifies that cascade merge combines non-null fields from both parent and child,
    /// with the child overriding when both specify the same field.
    /// </summary>
    [Test]
    public void CascadeMerge_ChildOverridesParentNonNullFields()
    {
        _fileSource.AddFile("Textures/.test-option.meta",
            "{\"Name\":\"A\"}");
        _fileSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Value\":10}");

        var cache = new TestOptionCache(_assetSystem);

        bool result = cache.TryGetOption("Textures/Weapons", out var option);

        Assert.IsTrue(result);
        Assert.AreEqual("A", option.Name, "Child should inherit parent Name when child Name is null.");
        Assert.AreEqual(10, option.Value, "Child Value should override parent (null) Value.");
    }

    /// <summary>
    /// Verifies that when the child specifies a field, it overrides the parent value,
    /// while other fields are inherited from the parent.
    /// </summary>
    [Test]
    public void CascadeMerge_ChildInheritsParentNullFields()
    {
        _fileSource.AddFile("Textures/.test-option.meta",
            "{\"Name\":\"A\",\"Value\":5}");
        _fileSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Name\":\"B\"}");

        var cache = new TestOptionCache(_assetSystem);

        bool result = cache.TryGetOption("Textures/Weapons", out var option);

        Assert.IsTrue(result);
        Assert.AreEqual("B", option.Name, "Child Name should override parent Name.");
        Assert.AreEqual(5, option.Value, "Child should inherit parent Value when child Value is null.");
    }

    /// <summary>
    /// Verifies that a three-level cascade correctly merges options from root,
    /// through mid, to leaf, with leaf overriding previous values.
    /// </summary>
    [Test]
    public void MultipleLevels_RootMidLeaf()
    {
        _fileSource.AddFile("Textures/.test-option.meta",
            "{\"Name\":\"root\"}");
        _fileSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Value\":10}");
        _fileSource.AddFile("Textures/Weapons/Swords/.test-option.meta",
            "{\"Name\":\"leaf\"}");

        var cache = new TestOptionCache(_assetSystem);

        bool result = cache.TryGetOption("Textures/Weapons/Swords", out var option);

        Assert.IsTrue(result);
        Assert.AreEqual("leaf", option.Name, "Leaf Name should override root Name.");
        Assert.AreEqual(10, option.Value, "Leaf should inherit Value from mid level.");
    }

    /// <summary>
    /// Verifies that <see cref="DirectoryOptionCache{TOption}.Invalidate"/> forces
    /// re-discovery of option files on the next query.
    /// </summary>
    [Test]
    public void Invalidate_ForceReDiscovery()
    {
        var cache = new TestOptionCache(_assetSystem);

        // First query caches the empty result.
        bool result1 = cache.TryGetOption("Textures/Weapons", out var option1);
        Assert.IsFalse(result1, "Should return false with no option files.");

        // Add a new option file — but the cache still holds stale data.
        _fileSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Name\":\"Fresh\",\"Value\":99}");

        // AssetSystem needs to be told to rebuild its file entries.
        _assetSystem.MarkEntriesDirty();

        // Still stale because DirectoryOptionCache cached the negative result.
        bool result2 = cache.TryGetOption("Textures/Weapons", out var option2);
        Assert.IsFalse(result2, "Should still be stale before invalidation.");

        // Invalidate forces re-discovery.
        cache.Invalidate();

        bool result3 = cache.TryGetOption("Textures/Weapons", out var option3);
        Assert.IsTrue(result3, "Should return true after invalidation re-discovers files.");
        Assert.AreEqual("Fresh", option3.Name);
        Assert.AreEqual(99, option3.Value);
    }

    /// <summary>
    /// Verifies that malformed JSON option files are silently skipped
    /// and do not prevent valid option files from being loaded.
    /// </summary>
    [Test]
    public void MalformedJson_SkippedGracefully()
    {
        _fileSource.AddFile("Textures/.test-option.meta",
            "this is not valid json!!!");
        _fileSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Name\":\"Valid\",\"Value\":7}");

        var cache = new TestOptionCache(_assetSystem);

        // Parent directory has a malformed file — should return false for that dir.
        bool rootResult = cache.TryGetOption("Textures", out var rootOption);
        Assert.IsFalse(rootResult, "Malformed JSON should be skipped, leaving no option.");

        // Child has a valid file — should still load correctly.
        bool childResult = cache.TryGetOption("Textures/Weapons", out var childOption);
        Assert.IsTrue(childResult, "Valid child option should still be discovered.");
        Assert.AreEqual("Valid", childOption.Name);
        Assert.AreEqual(7, childOption.Value);
    }
}
