using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using Alco.IO;

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
            Console.WriteLine($"[Error] {message}");
        }

        public void LogInfo(ReadOnlySpan<char> message)
        {
            Console.WriteLine($"[Info] {message}");
        }

        public void LogSuccess(ReadOnlySpan<char> message)
        {
            Console.WriteLine($"[Success] {message}");
        }

        public void LogWarning(ReadOnlySpan<char> message)
        {
            Console.WriteLine($"[Warning] {message}");
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
        public int Priority => 0;

        public IEnumerable<string> AllFileNames => ["test.fast", "test.slow"];

        public bool IsWriteable => false;

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

        public bool TryWriteData(string path, ReadOnlySpan<byte> data, out string failureReason)
        {
            throw new NotImplementedException();
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

        Profiler profiler = new Profiler();

        profiler.Start();
        Parallel.For(0, count, i =>
        {
            fastAssets[i] = assetSystem.Load<TestFastAsset>("test.fast");
        });

        TestContext.WriteLine($"Concurrent Load Time for fast asset: {profiler.End().Miliseconds}");

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

        
        profiler.Start();
        Parallel.For(0, count, i =>
        {
            slowAssets[i] = assetSystem.Load<TestSlowAsset>("test.slow");
        });

        TestContext.WriteLine($"Concurrent Load Time for slow asset: {profiler.End().Miliseconds}");

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
    public async void TestGarbagCollect()
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
        fastAsset = await assetSystem.LoadAsync<TestFastAsset>("test.fast");
        Assert.IsTrue(assetSystem.DebugIsAssetCached("test.fast"));

        fastAsset = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.IsFalse(assetSystem.DebugIsAssetCached("test.fast"));

        //strong cache
        fastAsset = assetSystem.Load<TestFastAsset>("test.fast", AssetCacheMode.Persistent);

        fastAsset = null;
        

        GC.Collect();
        GC.WaitForPendingFinalizers();

        Assert.IsTrue(assetSystem.DebugIsAssetCached("test.fast"));


        //assetSystem.Dispose();
    }

 
}