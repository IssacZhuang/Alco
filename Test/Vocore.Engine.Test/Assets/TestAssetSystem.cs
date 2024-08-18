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

    private class TestFastAssetLoader : IAssetLoader<TestFastAsset>
    {
        public string Name => "TestFastAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".fast"];

        public bool TryCreateAsset(string filename, ReadOnlySpan<byte> data, [NotNullWhen(true)] out TestFastAsset asset)
        {
            asset = new TestFastAsset();
            return true;
        }
    }

    private class TestSlowAssetLoader : IAssetLoader<TestSlowAsset>
    {
        public string Name => "TestSlowAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".slow"];

        public bool TryCreateAsset(string filename, ReadOnlySpan<byte> data, [NotNullWhen(true)] out TestSlowAsset asset)
        {
            asset = new TestSlowAsset();
            return true;
        }
    }

    private class TestFileSource : IFileSource
    {
        public int Order => 0;

        public IEnumerable<string> AllFileNames => ["test.fast", "test.slow"];

        public void OnUnload()
        {

        }

        public bool TryGetData(string path, [NotNullWhen(true)] out ReadOnlySpan<byte> data)
        {
            switch (path)
            {
                case "test.fast":
                    data = new byte[4];
                    return true;
                case "test.slow":
                    Thread.Sleep(500);
                    data = new byte[4];
                    return true;
                default:
                    data = default;
                    return false;
            }
        }
    }


    [Test]
    public void TestLoad()
    {
        AssetSystem assetSystem = new AssetSystem(2);
        assetSystem.RegisterAssetLoader(new TestFastAssetLoader());
        assetSystem.RegisterAssetLoader(new TestSlowAssetLoader());
        assetSystem.AddFileSource(new TestFileSource());

        var fastAsset = assetSystem.Load<TestFastAsset>("test.fast");
        Assert.NotNull(fastAsset);

        var slowAsset = assetSystem.Load<TestSlowAsset>("test.slow");
        Assert.NotNull(slowAsset);

        assetSystem.Dispose();
    }

    [Test]
    public void TestLoadConcurrent()
    {
        AssetSystem assetSystem = new AssetSystem(2);

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

        assetSystem.Dispose();
    }


}