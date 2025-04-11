using System.Text;
using NUnit.Framework;

namespace Alco.IO.Test;

#pragma warning disable CS0067

public class TestAssetSystemEncoder
{
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

    private class TestWriteableAsset
    {
        public string Data { get; set; }

        public TestWriteableAsset(string data)
        {
            Data = data;
        }
    }

    private class TestAssetEncoder : IAssetEncoder
    {
        public SafeMemoryHandle Encode(object asset)
        {
            if (asset is TestWriteableAsset writeableAsset)
            {
                return new SafeMemoryHandle(Encoding.UTF8.GetBytes(writeableAsset.Data));
            }
            throw new ArgumentException("Invalid asset type");
        }

        public IEnumerable<Type> GetSupportedTypes()
        {
            yield return typeof(TestWriteableAsset);
        }
    }

    private class WriteableTestFileSource : IFileSource
    {
        private readonly Dictionary<string, byte[]> _files = new();

        public int Priority => 0;

        public IEnumerable<string> AllFileNames => _files.Keys;

        public bool IsWriteable => true;

        public void Dispose() { }

        public WriteableTestFileSource()
        {
            //builtin asset to write
            _files["test2.asset"] = Array.Empty<byte>();
        }

        public bool TryGetData(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SafeMemoryHandle data, out string failedReason)
        {
            if (_files.TryGetValue(path, out var fileData))
            {
                data = new SafeMemoryHandle(fileData);
                failedReason = string.Empty;
                return true;
            }

            data = null;
            failedReason = "File not found";
            return false;
        }

        public bool TryWriteData(string path, ReadOnlySpan<byte> data, out string failureReason)
        {
            try
            {
                _files[path] = data.ToArray();
                failureReason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return false;
            }
        }
    }

    private AssetSystem _assetSystem;
    private WriteableTestFileSource _fileSource;
    private TestAssetEncoder _encoder;

    [SetUp]
    public void Setup()
    {
        var host = new LifeCycleProvider();
        _assetSystem = new AssetSystem(host, 2, false);
        _fileSource = new WriteableTestFileSource();
        _encoder = new TestAssetEncoder();

        _assetSystem.AddFileSource(_fileSource);
        _assetSystem.RegisterAssetEncoder(_encoder);
    }

    [Test]
    public void TestRegisterAndUnregisterEncoder()
    {
        var asset = new TestWriteableAsset("test data");

        // Should work with registered encoder
        Assert.DoesNotThrow(() => _assetSystem.EncodeToBinary(asset));

        // Unregister encoder
        _assetSystem.UnregisterAssetEncoder(_encoder);

        // Should throw after unregistering
        Assert.Throws<Exception>(() => _assetSystem.EncodeToBinary(asset));
    }

    [Test]
    public void TestWriteAsset()
    {
        var asset = new TestWriteableAsset("test data");
        const string path = "test2.asset";

        string failureReason;//for debug use
        // Test successful write
        Assert.True(_assetSystem.TryWriteAsset(path, asset, out failureReason));

        // Verify written data
        Assert.True(_fileSource.TryGetData(path, out var data, out _));
        Assert.That(Encoding.UTF8.GetString(data.Span), Is.EqualTo("test data"));
    }

    [Test]
    public void TestWriteAssetToNonExistentPath()
    {
        var asset = new TestWriteableAsset("test data");
        const string path = "nonexistent.asset";

        // Should fail when writing to non-existent path
        Assert.False(_assetSystem.TryWriteAsset(path, asset));
    }

    [Test]
    public void TestWriteAssetWithInvalidType()
    {
        var invalidAsset = new object();
        const string path = "test2.asset";

        // Should throw when trying to encode unsupported type
        Assert.Throws<Exception>(() => _assetSystem.WriteAsset(path, invalidAsset, typeof(object)));
    }
}