using System.Diagnostics.CodeAnalysis;
using System.Text;
using NUnit.Framework;
using Alco.IO;

namespace Alco.IO.Test;

public class TestAssetSystemStream
{
    private sealed class StreamTestHost : IAssetSystemHost, IDisposable
    {
        public event Action? OnDispose;

        public void Dispose() => OnDispose?.Invoke();

        public void LogError(ReadOnlySpan<char> message) { }
        public void LogInfo(ReadOnlySpan<char> message) { }
        public void LogSuccess(ReadOnlySpan<char> message) { }
        public void LogWarning(ReadOnlySpan<char> message) { }
        void IAssetSystemHost.PostToMainThread(Action action) => action();
    }

    private sealed class DisposableAsset : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class DisposableAssetLoader : IAssetLoader
    {
        public DisposableAsset Asset { get; } = new();

        public string Name => "DisposableAssetLoader";

        public IReadOnlyList<string> FileExtensions => [".disposable"];

        public bool CanHandleType(Type type) => type == typeof(DisposableAsset);

        public object CreateAsset(in AssetLoadContext context)
        {
            context.GetData();
            return Asset;
        }
    }

    private static string CreateTempAssetDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "alco_asset_stream_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static (AssetSystem System, StreamTestHost Host, string Directory) CreateSystem()
    {
        StreamTestHost host = new();
        AssetSystem system = new(host);
        string directory = CreateTempAssetDirectory();
        system.AddFileSource(new DirectoryFileSource(directory));
        return (system, host, directory);
    }

    [Test]
    public void TryGetStreamOpensSeekableStreamWithoutLoading()
    {
        (AssetSystem system, StreamTestHost host, string directory) = CreateSystem();
        try
        {
            byte[] content = Encoding.UTF8.GetBytes("stream asset payload");
            File.WriteAllBytes(Path.Combine(directory, "data.bin"), content);

            Assert.That(system.TryGetStream("data.bin", out Stream? stream), Is.True);
            Assert.That(stream, Is.Not.Null);
            using (stream!)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(stream.CanRead, Is.True);
                    Assert.That(stream.CanSeek, Is.True);
                    Assert.That(stream.Length, Is.EqualTo(content.Length));
                });

                stream.Seek(0, SeekOrigin.Begin);
                using MemoryStream buffer = new();
                stream.CopyTo(buffer);
                Assert.That(buffer.ToArray(), Is.EqualTo(content));
            }

            Assert.That(system.TryGetStream("missing.bin", out Stream? _), Is.False);
        }
        finally
        {
            host.Dispose();
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void UnloadRemovesCacheAndDisposesAsset()
    {
        (AssetSystem system, StreamTestHost host, string directory) = CreateSystem();
        DisposableAssetLoader loader = new();
        system.RegisterAssetLoader(loader);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "asset.disposable"), new byte[] { 1 });

            Assert.That(system.TryLoad("asset.disposable", out DisposableAsset? asset), Is.True);
            Assert.That(asset, Is.SameAs(loader.Asset));
            Assert.That(loader.Asset.IsDisposed, Is.False);

            Assert.That(system.Unload("asset.disposable"), Is.True);
            Assert.That(loader.Asset.IsDisposed, Is.True, "Unload must dispose cached disposable assets");

            Assert.That(system.Unload("asset.disposable"), Is.False, "second unload finds nothing cached");
        }
        finally
        {
            host.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
