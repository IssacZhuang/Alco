using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using Vocore.IO;

namespace Vocore.Engine.Test;

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
        public event Action OnHandleAssetLoaded;
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

    private class TestFastAssetLoader : IAssetLoader<TestFastAsset>
    {
        public string Name => "TestFastAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".fast"];

        public TestFastAsset CreateAsset(string filename, ReadOnlySpan<byte> data)
        {
            return new TestFastAsset();
        }
    }

    private class TestSlowAssetLoader : IAssetLoader<TestSlowAsset>
    {
        public string Name => "TestSlowAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".slow"];

        public TestSlowAsset CreateAsset(string filename, ReadOnlySpan<byte> data)
        {
            return new TestSlowAsset();
        }
    }

    //used for test error handling
    private class TestEmptyAssetLoader : IAssetLoader<TestFastAsset>
    {
        public string Name => "TestEmptyAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".empty"];

        public TestFastAsset CreateAsset(string filename, ReadOnlySpan<byte> data)
        {
            return null;
        }
    }

    private class TestExceptionAssetLoader : IAssetLoader<TestFastAsset>
    {
        public string Name => "TestExceptionAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".exception"];

        public TestFastAsset CreateAsset(string filename, ReadOnlySpan<byte> data)
        {
            throw new Exception("Test Exception");
        }
    }

    private class TestFileSource : IFileSource
    {
        public int Priority => 0;

        public IEnumerable<string> AllFileNames => ["test.fast", "test.slow"];

        public void Dispose()
        {

        }

        public bool TryGetData(string path, [NotNullWhen(true)] out ReadOnlySpan<byte> data, out string failedReason)
        {
            switch (path)
            {
                case "test.fast":
                    data = new byte[4];
                    failedReason = string.Empty;
                    return true;
                case "test.slow":
                    Thread.Sleep(500);
                    data = new byte[4];
                    failedReason = string.Empty;
                    return true;
                case "test.empty":
                    data = Array.Empty<byte>();
                    failedReason = string.Empty;
                    return true;
                case "test.exception":
                    data = new byte[4];
                    failedReason = string.Empty;
                    return true;
                default:
                    data = default;
                    failedReason = string.Empty;
                    return false;
            }
        }
    }


    [Test]
    public void TestLoad()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider, 2);
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
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider, 2);

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
    public void TestLoadAsync()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider, 2);
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.RegisterAssetLoader(new TestEmptyAssetLoader());
        assetSystem.RegisterAssetLoader(new TestExceptionAssetLoader());
        assetSystem.AddFileSource(new TestFileSource());

        assetSystem.LoadAsync<TestFastAsset>("test.fast", (asset, exception) =>
        {
            Assert.IsNull(exception);
            Assert.NotNull(asset);
        });

        assetSystem.LoadAsync<TestSlowAsset>("test.slow", (asset, exception) =>
        {
            Assert.IsNull(exception);
            Assert.NotNull(asset);
        });

        assetSystem.LoadAsync<TestFastAsset>("test.empty", (asset, exception) =>
        {
            Assert.NotNull(exception);
            Assert.IsNull(asset);
        });

        assetSystem.LoadAsync<TestFastAsset>("test.exception", (asset, exception) =>
        {
            Assert.NotNull(exception);
            Assert.IsNull(asset);
        });

        int count = assetSystem.DebugWaitForAllJobComplete();

        Assert.That(count, Is.EqualTo(4));

        //assetSystem.Dispose();
    }

    [Test]
    public async Task TestLoadAsyncTask()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider, 2);
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.RegisterAssetLoader(new TestEmptyAssetLoader());
        assetSystem.RegisterAssetLoader(new TestExceptionAssetLoader());
        assetSystem.AddFileSource(new TestFileSource());

        var fastAsset = await assetSystem.LoadAsyncTask<TestFastAsset>("test.fast");
        Assert.NotNull(fastAsset);

        var slowAsset = await assetSystem.LoadAsyncTask<TestSlowAsset>("test.slow");
        Assert.NotNull(slowAsset);

        Assert.CatchAsync<AssetLoadException>(async () =>
        {
            await assetSystem.LoadAsyncTask<TestFastAsset>("test.empty");
        });

        Assert.CatchAsync<AssetLoadException>(async () =>
        {
            await assetSystem.LoadAsyncTask<TestFastAsset>("test.exception");
        });
    }

    [Test]
    public void TestLoadAsyncConcurrent()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider, 2);

        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.RegisterAssetLoader(new TestEmptyAssetLoader());
        assetSystem.RegisterAssetLoader(new TestExceptionAssetLoader());

        assetSystem.AddFileSource(new TestFileSource());

        int count = 50;
        TestFastAsset[] fastAssets = new TestFastAsset[count];
        Exception[] fastExceptions = new Exception[count];
        TestSlowAsset[] slowAssets = new TestSlowAsset[count];
        Exception[] slowExceptions = new Exception[count];

        Profiler profiler = new Profiler();

        profiler.Start();
        Parallel.For(0, count * 2, i =>
        {
            if (i % 2 == 0)
            {
                assetSystem.LoadAsync<TestFastAsset>("test.fast", (asset, exception) =>
                {
                    fastAssets[i / 2] = asset;
                    fastExceptions[i / 2] = exception;
                });
            }
            else
            {
                assetSystem.LoadAsync<TestSlowAsset>("test.slow", (asset, exception) =>
                {
                    slowAssets[i / 2] = asset;
                    slowExceptions[i / 2] = exception;
                });
            }
        });

        TestContext.WriteLine($"dispatch load job: {profiler.End().Miliseconds}");

        profiler.Start();
        int jobCount = assetSystem.DebugWaitForAllJobComplete();

        TestContext.WriteLine($"Wait for all job complete: {profiler.End().Miliseconds}");

        //only 2 jobs for fast and slow, not 100
        Assert.That(jobCount, Is.EqualTo(2));

        TestFastAsset checkFastAsset = null;
        TestSlowAsset checkSlowAsset = null;
        for (int i = 0; i < count; i++)
        {
            if (checkFastAsset == null)
            {
                checkFastAsset = fastAssets[i];
            }
            else
            {
                Assert.That(checkFastAsset, Is.SameAs(fastAssets[i]));
            }

            if (checkSlowAsset == null)
            {
                checkSlowAsset = slowAssets[i];
            }
            else
            {
                Assert.That(checkSlowAsset, Is.SameAs(slowAssets[i]));
            }

            Assert.IsNull(fastExceptions[i]);
            Assert.IsNull(slowExceptions[i]);
        }

        Assert.NotNull(checkFastAsset);
        Assert.NotNull(checkSlowAsset);

        object[] emptyAssets = new object[count];
        Exception[] emptyExceptions = new Exception[count];

        object[] exceptionAssets = new object[count];
        Exception[] exceptionExceptions = new Exception[count];
        //test error handling
        Parallel.For(0, count * 2, i =>
        {
            if (i % 2 == 0)
            {
                assetSystem.LoadAsync<TestFastAsset>("test.empty", (asset, exception) =>
                {
                    emptyAssets[i / 2] = asset;
                    emptyExceptions[i / 2] = exception;
                });
            }
            else
            {
                assetSystem.LoadAsync<TestFastAsset>("test.exception", (asset, exception) =>
                {
                    exceptionAssets[i / 2] = asset;
                    exceptionExceptions[i / 2] = exception;
                });
            }
        });

        jobCount = assetSystem.DebugWaitForAllJobComplete();

        Assert.That(jobCount, Is.EqualTo(2));

        for (int i = 0; i < count; i++)
        {
            Assert.IsNull(emptyAssets[i]);
            Assert.NotNull(emptyExceptions[i]);

            Assert.IsNull(exceptionAssets[i]);
            Assert.NotNull(exceptionExceptions[i]);
        }

        //assetSystem.Dispose();
    }

    [Test]
    public void TestLoadSyncWithAsyncConcurrent()
    {
        int count = 50;
        // half is Load, half is LoadAsync
        // in parallel  
        //use slow asset only
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider, 2);

        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.RegisterAssetLoader(new TestEmptyAssetLoader());
        assetSystem.RegisterAssetLoader(new TestExceptionAssetLoader());

        assetSystem.AddFileSource(new TestFileSource());

       

        TestSlowAsset[] slowAssets = new TestSlowAsset[count];
        Exception[] slowExceptions = new Exception[count];

        Profiler profiler = new Profiler();

        profiler.Start();

        Parallel.For(0, count * 2, i =>
        {
            if (i % 2 == 0)
            {
                assetSystem.LoadAsync<TestSlowAsset>("test.slow", (asset, exception) =>
                {
                    slowAssets[i / 2] = asset;
                    slowExceptions[i / 2] = exception;
                });
                
            }
            else
            {
                slowAssets[i / 2] = assetSystem.Load<TestSlowAsset>("test.slow");
            }
        });

        TestContext.WriteLine($"TestLoadSyncWithAsyncConcurrent dispatch load job: {profiler.End().Miliseconds}");

        profiler.Start();
        int jobCount = assetSystem.DebugWaitForAllJobComplete();

        TestContext.WriteLine($"TestLoadSyncWithAsyncConcurrent Wait for all job complete: {profiler.End().Miliseconds}");

        //Assert.That(jobCount, Is.EqualTo(1));

        TestSlowAsset checkSlowAsset = null;
        for (int i = 0; i < count; i++)
        {
            if (checkSlowAsset == null)
            {
                checkSlowAsset = slowAssets[i];
            }
            else
            {
                Assert.That(checkSlowAsset, Is.SameAs(slowAssets[i]));
            }

            Assert.IsNull(slowExceptions[i]);
        }

        Assert.NotNull(checkSlowAsset);

        //assetSystem.Dispose();

    }

    [Test]
    public void TestGarbagCollect()
    {
        using LifeCycleProvider lifeCycleProvider = new LifeCycleProvider();
        AssetSystem assetSystem = new AssetSystem(lifeCycleProvider, 2);
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
        assetSystem.LoadAsync<TestFastAsset>("test.fast", (asset, exception) =>{
            fastAsset = asset;
            asset = null;
        });
        assetSystem.DebugWaitForAllJobComplete();
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