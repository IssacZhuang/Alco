using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using Alco.IO;
using System.Collections.Concurrent;
using System.Text;

namespace Alco.IO.Test;

#pragma warning disable CS0067

public class TestAssetSystem
{
    private class TestFastAsset
    {
        public byte[] Data { get; }
        public TestFastAsset()
        {
            Data = new byte[1024];
        }
    }

    private class TestSlowAsset
    {
        public byte[] Data { get; }
        public TestSlowAsset()
        {
            Thread.Sleep(500);
            Data = new byte[1024];

        }
    }

    private class LifeCycleProvider : IAssetSystemHost, IDisposable
    {
        public event Action OnDispose;

        public void Dispose()
        {
            OnDispose?.Invoke();
        }

        public void LogError(ReadOnlySpan<char> message)
        {
            
        }

        public void LogInfo(ReadOnlySpan<char> message)
        {
            
        }

        public void LogSuccess(ReadOnlySpan<char> message)
        {
             
        }

        public void LogWarning(ReadOnlySpan<char> message)
        {
            
        }

        void IAssetSystemHost.PostToMainThread(Action action)
        {
            
        }
    }

    private class TestFastAssetLoader : IAssetLoader
    {
        public string Name => "TestFastAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".fast"];

        public bool CanHandleType(Type type)
        {
            return type == typeof(TestFastAsset);
        }

        public object CreateAsset(in AssetLoadContext context)
        {
            return new TestFastAsset();
        }
    }

    private class TestSlowAssetLoader : IAssetLoader
    {
        public string Name => "TestSlowAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".slow"];

        public bool CanHandleType(Type type)
        {
            return type == typeof(TestSlowAsset);
        }

        public object CreateAsset(in AssetLoadContext context)
        {
            Thread.Sleep(100);
            return new TestSlowAsset();
        }
    }

    //used for test error handling
    private class TestEmptyAssetLoader : IAssetLoader
    {
        public string Name => "TestEmptyAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".empty"];

        public bool CanHandleType(Type type)
        {
            return type == typeof(TestFastAsset);
        }

        public object CreateAsset(in AssetLoadContext context)
        {
            return null!;
        }
    }

    private class TestExceptionAssetLoader : IAssetLoader
    {
        public string Name => "TestExceptionAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".exception"];

        public bool CanHandleType(Type type)
        {
            return type == typeof(TestFastAsset);
        }

        public object CreateAsset(in AssetLoadContext context)
        {
            throw new Exception("Test exception");
        }
    }

    private class TestFileSource : IFileSource
    {
        public string Name => "Test File Source";

        public int Priority => 0;

        public IEnumerable<string> AllFileNames => ["test.fast", "test.slow"];

        public void Dispose()
        {

        }

        public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, out string failedReason)
        {
            switch (path)
            {
                case "test.fast":
                    data = new SafeMemoryHandle(new byte[4]);
                    failedReason = string.Empty;
                    return true;
                case "test.slow":
                    Thread.Sleep(500);
                    data = new SafeMemoryHandle(new byte[4]);
                    failedReason = string.Empty;
                    return true;
                case "test.empty":
                    data = SafeMemoryHandle.Empty;
                    failedReason = string.Empty;
                    return true;
                case "test.exception":
                    data = new SafeMemoryHandle(new byte[4]);
                    failedReason = string.Empty;
                    return true;
                default:
                    data = default;
                    failedReason = string.Empty;
                    return false;
            }
        }

        public bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
        {
            switch (path)
            {
                case "test.fast":
                case "test.slow":
                case "test.exception":
                    stream = new MemoryStream(new byte[] { 0, 1, 2, 3 });
                    failureReason = null;
                    return true;
                case "test.empty":
                    stream = new MemoryStream(Array.Empty<byte>());
                    failureReason = null;
                    return true;
                default:
                    stream = null;
                    failureReason = "File not found";
                    return false;
            }
        }
    }


    [Test]
    public void TestLoad()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider);
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.RegisterAssetLoader(new TestEmptyAssetLoader());
        assetSystem.RegisterAssetLoader(new TestExceptionAssetLoader());
        assetSystem.AddFileSource(new TestFileSource());

        var fastAsset = assetSystem.Load<TestFastAsset>("test.fast");
        Assert.NotNull(fastAsset);

        var slowAsset = assetSystem.Load<TestSlowAsset>("test.slow");
        Assert.NotNull(slowAsset);

        Assert.Catch<AssetLoadException>(() =>
        {
            assetSystem.Load<TestFastAsset>("test.empty");
        });

        Assert.Catch<AssetLoadException>(() =>
        {
            assetSystem.Load<TestFastAsset>("test.exception");
        });

        //no loader
        Assert.Catch<AssetLoadException>(() =>
        {
            assetSystem.Load<TestFastAsset>("test.noLoader");
        });

        //assetSystem.Dispose();
    }

    [Test]
    public void TestLoadConcurrent()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider);

        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.AddFileSource(new TestFileSource());

        int count = 100;
        TestFastAsset[] fastAssets = new TestFastAsset[count];
        TestSlowAsset[] slowAssets = new TestSlowAsset[count];

        long startFast = Stopwatch.GetTimestamp();
        Parallel.For(0, count, i =>
        {
            fastAssets[i] = assetSystem.Load<TestFastAsset>("test.fast");
        });
        long elapsedFast = Stopwatch.GetTimestamp() - startFast;
        double msFast = Math.Round(elapsedFast * (1000.0 / Stopwatch.Frequency), 3);
        TestContext.WriteLine($"Concurrent Load Time for fast asset: {msFast}");

        TestFastAsset check = null;
        //all assets in the array should be the same
        foreach (var asset in fastAssets)
        {
            if (check == null)
            {
                check = asset;
            }
            else
            {
                Assert.That(check, Is.SameAs(asset));
            }
        }

        
        long startSlow = Stopwatch.GetTimestamp();
        Parallel.For(0, count, i =>
        {
            slowAssets[i] = assetSystem.Load<TestSlowAsset>("test.slow");
        });
        long elapsedSlow = Stopwatch.GetTimestamp() - startSlow;
        double msSlow = Math.Round(elapsedSlow * (1000.0 / Stopwatch.Frequency), 3);
        TestContext.WriteLine($"Concurrent Load Time for slow asset: {msSlow}");

        TestSlowAsset checkSlow = null;
        //all assets in the array should be the same
        foreach (var asset in slowAssets)
        {
            if (checkSlow == null)
            {
                checkSlow = asset;
            }
            else
            {
                Assert.That(checkSlow, Is.SameAs(asset));
            }
        }

        //assetSystem.Dispose();
    }


    [Test]
    public async Task TestLoadAsyncTask()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider);
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.RegisterAssetLoader(new TestEmptyAssetLoader());
        assetSystem.RegisterAssetLoader(new TestExceptionAssetLoader());
        assetSystem.AddFileSource(new TestFileSource());

        var fastAsset = await assetSystem.LoadAsync<TestFastAsset>("test.fast");
        Assert.NotNull(fastAsset);

        var slowAsset = await assetSystem.LoadAsync<TestSlowAsset>("test.slow");
        Assert.NotNull(slowAsset);

        Assert.CatchAsync<AssetLoadException>(async () =>
        {
            await assetSystem.LoadAsync<TestFastAsset>("test.empty");
        });

        Assert.CatchAsync<AssetLoadException>(async () =>
        {
            await assetSystem.LoadAsync<TestFastAsset>("test.exception");
        });
    }


   
// the garbage collect is lazy in debug mode
// it might cause the test failed
#if !DEBUG
    [Test]
#endif
    public void TestGarbagCollect()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider);
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.AddFileSource(new TestFileSource());

        var fastAsset = assetSystem.Load<TestFastAsset>("test.fast", AssetCacheMode.None);

        Assert.IsFalse(assetSystem.DebugIsAssetCached("test.fast"));

        //AssetCacheMode.Recyclable by default
        fastAsset = assetSystem.Load<TestFastAsset>("test.fast", AssetCacheMode.Recyclable);

        //before release reference
        Assert.IsTrue(assetSystem.DebugIsAssetCached("test.fast"));
        //release reference
        fastAsset = null;

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.IsFalse(assetSystem.DebugIsAssetCached("test.fast"));

        //async load
        // this test is disabled because the asset might be hold by the async state machine and not be collected during the test
        // fastAsset = await assetSystem.LoadAsync<TestFastAsset>("test.fast");
        // Assert.IsTrue(assetSystem.DebugIsAssetCached("test.fast"));

        // fastAsset = null;
        // GC.Collect();
        // GC.WaitForPendingFinalizers();

        // Assert.IsFalse(assetSystem.DebugIsAssetCached("test.fast"));

        //strong cache
        fastAsset = assetSystem.Load<TestFastAsset>("test.fast", AssetCacheMode.Persistent);

        fastAsset = null;
        

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.IsTrue(assetSystem.DebugIsAssetCached("test.fast"));


        //assetSystem.Dispose();
    }

    private class VirtualFileSource : IFileSource
    {
        public string Name => "Virtual File Source";

        private readonly ConcurrentDictionary<string, string> _files = new();

        public int Priority => 0;

        public IEnumerable<string> AllFileNames => _files.Keys;


        public void SetFile(string key, string text)
        {
            _files[key] = text;
        }

        public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, out string failureReason)
        {
            if (_files.TryGetValue(path, out var content))
            {
                data = new SafeMemoryHandle(Encoding.UTF8.GetBytes(content));
                failureReason = string.Empty;
                return true;
            }
            data = default;
            failureReason = "File not found";
            return false;
        }

        public bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
        {
            if (_files.TryGetValue(path, out var content))
            {
                byte[] contentBytes = Encoding.UTF8.GetBytes(content);
                stream = new MemoryStream(contentBytes);
                failureReason = null;
                return true;
            }
            stream = null;
            failureReason = "File not found";
            return false;
        }
    }

    private class TestAssetLoader : IAssetLoader
    {
        public string Name => "Text loader";

        public IReadOnlyList<string> FileExtensions => [".txt"];

        public bool CanHandleType(Type type)
        {
            return type == typeof(string);
        }

        public object CreateAsset(in AssetLoadContext context)
        {
            return Encoding.UTF8.GetString(context.Data);
        }
    }

    [Test]
    public void TestListAssets()
    {
        AssetSystem assetSystem = new AssetSystem(new LifeCycleProvider());
        VirtualFileSource fileSource = new VirtualFileSource();
        fileSource.SetFile("test.txt", "test");
        fileSource.SetFile("path1.txt", "test");
        fileSource.SetFile("path1/test.txt", "test");
        fileSource.SetFile("path1/test2.txt", "test");
        fileSource.SetFile("path1/subPath/test.txt", "test");
        fileSource.SetFile("path2/test.txt", "test");
        fileSource.SetFile("path2/test2.txt", "test");
        assetSystem.AddFileSource(fileSource);

        assetSystem.RegisterAssetLoader(new TestAssetLoader());

        Assert.That(assetSystem.ListAssetsInPath("").Count(), Is.EqualTo(7));

        List<string> files = new();
        foreach (var file in assetSystem.ListAssetsInPath("path1"))
        {
            files.Add(file);
        }
        Assert.That(files.Count, Is.EqualTo(3));
        Assert.That(files.Contains("test.txt"), Is.False);
        Assert.That(files.Contains("path1.txt"), Is.False);
        Assert.That(files.Contains("path1/test.txt"), Is.True);
        Assert.That(files.Contains("path1/test2.txt"), Is.True);
        Assert.That(files.Contains("path1/subPath/test.txt"), Is.True);
        Assert.That(files.Contains("path2/test.txt"), Is.False);
        Assert.That(files.Contains("path2/test2.txt"), Is.False);

        files.Clear();
        foreach (var file in assetSystem.ListAssetsInPath("path1/"))
        {
            files.Add(file);
        }
        Assert.That(files.Count, Is.EqualTo(3));
        Assert.That(files.Contains("test.txt"), Is.False);
        Assert.That(files.Contains("path1.txt"), Is.False);
        Assert.That(files.Contains("path1/test.txt"), Is.True);
        Assert.That(files.Contains("path1/test2.txt"), Is.True);
        Assert.That(files.Contains("path1/subPath/test.txt"), Is.True);
        Assert.That(files.Contains("path2/test.txt"), Is.False);
        Assert.That(files.Contains("path2/test2.txt"), Is.False);



    }

    [Test]
    public void TestGetExtensionsForType()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider);

        // Setup loaders
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());    // .fast for TestFastAsset
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());    // .slow for TestSlowAsset  
        assetSystem.RegisterAssetLoader(new TestAssetLoader());        // .txt for string

        // Test getting extensions for TestFastAsset
        var fastExtensions = assetSystem.GetExtensionsForType(typeof(TestFastAsset));
        Assert.That(fastExtensions.Count, Is.EqualTo(1));
        Assert.That(fastExtensions.Contains(".fast"), Is.True);

        // Test getting extensions for TestSlowAsset
        var slowExtensions = assetSystem.GetExtensionsForType(typeof(TestSlowAsset));
        Assert.That(slowExtensions.Count, Is.EqualTo(1));
        Assert.That(slowExtensions.Contains(".slow"), Is.True);

        // Test getting extensions for string
        var stringExtensions = assetSystem.GetExtensionsForType(typeof(string));
        Assert.That(stringExtensions.Count, Is.EqualTo(1));
        Assert.That(stringExtensions.Contains(".txt"), Is.True);

        // Test getting extensions for unsupported type
        var unsupportedExtensions = assetSystem.GetExtensionsForType(typeof(int));
        Assert.That(unsupportedExtensions.Count, Is.EqualTo(0));

        // Test caching - calling again should return same instance
        var fastExtensions2 = assetSystem.GetExtensionsForType(typeof(TestFastAsset));
        Assert.That(fastExtensions2, Is.SameAs(fastExtensions));

        // Test null argument
        Assert.Throws<ArgumentNullException>(() => assetSystem.GetExtensionsForType(null!));
    }

    [Test]
    public void TestGetExtensionsForTypeCacheInvalidation()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider);

        // Initially no extensions for TestFastAsset
        var extensions1 = assetSystem.GetExtensionsForType(typeof(TestFastAsset));
        Assert.That(extensions1.Count, Is.EqualTo(0));

        // Register a loader - use the same instance for register and unregister
        var testLoader = new TestFastAssetLoader();
        assetSystem.RegisterAssetLoader(testLoader);

        // Cache should be invalidated, now should find extensions
        var extensions2 = assetSystem.GetExtensionsForType(typeof(TestFastAsset));
        Assert.That(extensions2.Count, Is.EqualTo(1));
        Assert.That(extensions2.Contains(".fast"), Is.True);
        Assert.That(extensions2, Is.Not.SameAs(extensions1));

        // Unregister the same loader instance
        assetSystem.UnregisterAssetLoader(testLoader);

        // Cache should be invalidated again, should be empty
        var extensions3 = assetSystem.GetExtensionsForType(typeof(TestFastAsset));
        Assert.That(extensions3.Count, Is.EqualTo(0));
        Assert.That(extensions3, Is.Not.SameAs(extensions2));
    }

    [Test]
    public void TestListAssetsInPathWithType()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider);

        // Setup file source with various file types
        VirtualFileSource fileSource = new VirtualFileSource();
        fileSource.SetFile("root.txt", "test");
        fileSource.SetFile("root.fast", "test");
        fileSource.SetFile("root.slow", "test");
        fileSource.SetFile("root.unknown", "test");
        fileSource.SetFile("path1/test.txt", "test");
        fileSource.SetFile("path1/test.fast", "test");
        fileSource.SetFile("path1/test.slow", "test");
        fileSource.SetFile("path1/subPath/nested.txt", "test");
        fileSource.SetFile("path1/subPath/nested.fast", "test");
        fileSource.SetFile("path2/another.txt", "test");
        fileSource.SetFile("path2/another.fast", "test");
        assetSystem.AddFileSource(fileSource);

        // Setup loaders
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());    // .fast for TestFastAsset
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());    // .slow for TestSlowAsset
        assetSystem.RegisterAssetLoader(new TestAssetLoader());        // .txt for string

        // Test listing all string assets (*.txt) in root
        var stringAssets = assetSystem.ListAssetsInPath("", typeof(string)).ToList();
        Assert.That(stringAssets.Count, Is.EqualTo(4));
        Assert.That(stringAssets.Contains("root.txt"), Is.True);
        Assert.That(stringAssets.Contains("path1/test.txt"), Is.True);
        Assert.That(stringAssets.Contains("path1/subPath/nested.txt"), Is.True);
        Assert.That(stringAssets.Contains("path2/another.txt"), Is.True);
        Assert.That(stringAssets.Contains("root.fast"), Is.False);
        Assert.That(stringAssets.Contains("root.slow"), Is.False);
        Assert.That(stringAssets.Contains("root.unknown"), Is.False);

        // Test listing TestFastAsset assets (*.fast) in path1
        var fastAssets = assetSystem.ListAssetsInPath("path1", typeof(TestFastAsset)).ToList();
        Assert.That(fastAssets.Count, Is.EqualTo(2));
        Assert.That(fastAssets.Contains("path1/test.fast"), Is.True);
        Assert.That(fastAssets.Contains("path1/subPath/nested.fast"), Is.True);
        Assert.That(fastAssets.Contains("path1/test.txt"), Is.False);
        Assert.That(fastAssets.Contains("path1/test.slow"), Is.False);

        // Test listing TestSlowAsset assets (*.slow) in path1
        var slowAssets = assetSystem.ListAssetsInPath("path1", typeof(TestSlowAsset)).ToList();
        Assert.That(slowAssets.Count, Is.EqualTo(1));
        Assert.That(slowAssets.Contains("path1/test.slow"), Is.True);

        // Test listing unsupported type - should return empty
        var unsupportedAssets = assetSystem.ListAssetsInPath("", typeof(int)).ToList();
        Assert.That(unsupportedAssets.Count, Is.EqualTo(0));

        // Test with empty path
        var allFastAssets = assetSystem.ListAssetsInPath("", typeof(TestFastAsset)).ToList();
        Assert.That(allFastAssets.Count, Is.EqualTo(4));
        Assert.That(allFastAssets.Contains("root.fast"), Is.True);
        Assert.That(allFastAssets.Contains("path1/test.fast"), Is.True);
        Assert.That(allFastAssets.Contains("path1/subPath/nested.fast"), Is.True);
        Assert.That(allFastAssets.Contains("path2/another.fast"), Is.True);

        // Test with non-existent path
        var nonExistentAssets = assetSystem.ListAssetsInPath("nonexistent", typeof(string)).ToList();
        Assert.That(nonExistentAssets.Count, Is.EqualTo(0));

        // Test null type argument
        Assert.Throws<ArgumentNullException>(() => assetSystem.ListAssetsInPath("", null!).ToList());
    }

    [Test]
    public void TestListAssetsInPathWithTypeThreadSafety()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider);

        // Setup file source
        VirtualFileSource fileSource = new VirtualFileSource();
        for (int i = 0; i < 100; i++)
        {
            fileSource.SetFile($"test{i}.txt", "test");
            fileSource.SetFile($"test{i}.fast", "test");
        }
        assetSystem.AddFileSource(fileSource);

        // Setup loaders
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestAssetLoader());

        // Test concurrent access
        var tasks = new List<Task>();
        var results = new ConcurrentBag<List<string>>();

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var stringAssets = assetSystem.ListAssetsInPath("", typeof(string)).ToList();
                results.Add(stringAssets);
            }));

            tasks.Add(Task.Run(() =>
            {
                var fastAssets = assetSystem.ListAssetsInPath("", typeof(TestFastAsset)).ToList();
                results.Add(fastAssets);
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Verify all results are consistent
        var stringResults = results.Where(r => r.Count > 0 && r[0].EndsWith(".txt")).ToList();
        var fastResults = results.Where(r => r.Count > 0 && r[0].EndsWith(".fast")).ToList();

        Assert.That(stringResults.Count, Is.EqualTo(10));
        Assert.That(fastResults.Count, Is.EqualTo(10));

        // All string results should be identical
        for (int i = 1; i < stringResults.Count; i++)
        {
            Assert.That(stringResults[i].Count, Is.EqualTo(stringResults[0].Count));
            Assert.That(stringResults[i], Is.EquivalentTo(stringResults[0]));
        }

        // All fast results should be identical
        for (int i = 1; i < fastResults.Count; i++)
        {
            Assert.That(fastResults[i].Count, Is.EqualTo(fastResults[0].Count));
            Assert.That(fastResults[i], Is.EquivalentTo(fastResults[0]));
        }
    }


}