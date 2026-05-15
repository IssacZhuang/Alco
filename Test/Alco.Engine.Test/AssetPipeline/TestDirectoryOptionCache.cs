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
    /// re-discovery of option files on the next query, and that version-based
    /// auto-invalidation also works via <see cref="AssetSystem.MarkEntriesDirty"/>.
    /// </summary>
    [Test]
    public void Invalidate_ForceReDiscovery()
    {
        var cache = new TestOptionCache(_assetSystem);

        // First query caches the empty result.
        bool result1 = cache.TryGetOption("Textures/Weapons", out var option1);
        Assert.IsFalse(result1, "Should return false with no option files.");

        // Add a new option file.
        _fileSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Name\":\"Fresh\",\"Value\":99}");

        // MarkEntriesDirty increments Version, so TryGetOption auto-invalidates.
        _assetSystem.MarkEntriesDirty();

        bool result2 = cache.TryGetOption("Textures/Weapons", out var option2);
        Assert.IsTrue(result2, "Should return true after MarkEntriesDirty triggers auto-invalidation.");
        Assert.AreEqual("Fresh", option2.Name);
        Assert.AreEqual(99, option2.Value);

        // Manual Invalidate also works — clears cache so next query re-discovers.
        cache.Invalidate();
        _fileSource.RemoveFile("Textures/Weapons/.test-option.meta");
        _assetSystem.MarkEntriesDirty();

        bool result3 = cache.TryGetOption("Textures/Weapons", out var option3);
        Assert.IsFalse(result3, "Should return false after file removed and cache invalidated.");
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

    /// <summary>
    /// Verifies that adding a file source increments <see cref="AssetSystem.Version"/>
    /// and the cache auto-invalidates on next query.
    /// </summary>
    [Test]
    public void FileSourceAdded_VersionBumpsAndCacheInvalidates()
    {
        // Initial query with no files — caches negative result
        var cache = new TestOptionCache(_assetSystem);
        int versionBefore = _assetSystem.Version;
        bool result1 = cache.TryGetOption("Textures/Weapons", out var option1);
        Assert.IsFalse(result1, "Should return false with no option files.");

        // Add a file source with an option file
        var newSource = new TestFileSource();
        newSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Name\":\"Fresh\",\"Value\":99}");
        _assetSystem.AddFileSource(newSource);

        // Version should have incremented
        Assert.Greater(_assetSystem.Version, versionBefore,
            "Version should increment after AddFileSource.");

        // Cache should auto-invalidate via version check
        bool result2 = cache.TryGetOption("Textures/Weapons", out var option2);
        Assert.IsTrue(result2, "Should return true after file source added.");
        Assert.AreEqual("Fresh", option2.Name);
        Assert.AreEqual(99, option2.Value);

        newSource.Dispose();
    }

    /// <summary>
    /// Verifies that removing a file source increments <see cref="AssetSystem.Version"/>
    /// and the cache auto-invalidates, returning false for removed options.
    /// </summary>
    [Test]
    public void FileSourceRemoved_VersionBumpsAndCacheInvalidates()
    {
        _fileSource.AddFile("Textures/Weapons/.test-option.meta",
            "{\"Name\":\"Sword\",\"Value\":42}");

        var cache = new TestOptionCache(_assetSystem);
        bool result1 = cache.TryGetOption("Textures/Weapons", out var option1);
        Assert.IsTrue(result1);
        Assert.AreEqual("Sword", option1.Name);

        int versionBefore = _assetSystem.Version;

        // Remove the file source
        _assetSystem.RemoveFileSource(_fileSource);

        Assert.Greater(_assetSystem.Version, versionBefore,
            "Version should increment after RemoveFileSource.");

        // Cache should auto-invalidate — no options now
        bool result2 = cache.TryGetOption("Textures/Weapons", out var option2);
        Assert.IsFalse(result2, "Should return false after file source removed.");
    }

    /// <summary>
    /// Verifies that <see cref="AssetSystem.MarkEntriesDirty"/> increments Version.
    /// </summary>
    [Test]
    public void MarkEntriesDirty_VersionBumps()
    {
        int version0 = _assetSystem.Version;
        _assetSystem.MarkEntriesDirty();
        Assert.Greater(_assetSystem.Version, version0,
            "Version should increment after MarkEntriesDirty.");

        int version1 = _assetSystem.Version;
        _assetSystem.MarkEntriesDirty();
        Assert.Greater(_assetSystem.Version, version1,
            "Version should increment again on second MarkEntriesDirty.");
    }

    /// <summary>
    /// Verifies that concurrent calls to <see cref="DirectoryOptionCache{TOption}.TryGetOption"/>
    /// from multiple threads produce consistent results without exceptions.
    /// </summary>
    [Test]
    public void ConcurrentAccess_ParallelFor()
    {
        _fileSource.AddFile("Root/.test-option.meta",
            "{\"Name\":\"Root\",\"Value\":1}");
        _fileSource.AddFile("Root/Child/.test-option.meta",
            "{\"Value\":2}");

        var cache = new TestOptionCache(_assetSystem);
        string? lastName = null;
        int? lastValue = null;
        bool firstResult = true;
        Exception? error = null;

        Parallel.For(0, 200, i =>
        {
            try
            {
                bool result = cache.TryGetOption("Root/Child", out var option);
                if (result)
                {
                    // Thread-safe read of first result
                    if (firstResult)
                    {
                        firstResult = false;
                        lastName = option.Name;
                        lastValue = option.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.IsNull(error, $"Concurrent access threw exception: {error?.Message}");

        // Verify the first result was correct
        Assert.IsFalse(firstResult, "Should have found an option.");
        Assert.AreEqual("Root", lastName, "All concurrent reads should get same merged Name.");
        Assert.AreEqual(2, lastValue, "All concurrent reads should get same merged Value.");
    }

    /// <summary>
    /// Verifies that concurrent access combined with <see cref="DirectoryOptionCache{TOption}.Invalidate"/>
    /// does not crash.
    /// </summary>
    [Test]
    public void ConcurrentAccess_WithInvalidation()
    {
        _fileSource.AddFile("Textures/.test-option.meta",
            "{\"Name\":\"Test\",\"Value\":5}");

        var cache = new TestOptionCache(_assetSystem);
        Exception? error = null;

        Parallel.For(0, 200, i =>
        {
            try
            {
                if (i % 10 == 0)
                {
                    cache.Invalidate();
                }
                else
                {
                    cache.TryGetOption("Textures", out _);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Assert.IsNull(error, $"Concurrent access with invalidation threw exception: {error?.Message}");
    }
}
