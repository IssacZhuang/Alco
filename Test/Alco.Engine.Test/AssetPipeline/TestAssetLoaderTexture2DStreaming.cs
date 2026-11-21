using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.IO;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>Covers Texture2D asset streaming, including header probing, in-place uploads, and decode fallbacks.</summary>
[TestFixture]
public class TestAssetLoaderTexture2DStreaming
{
    private class LifeCycleProvider : IAssetSystemHost, IDisposable
    {
        public LifeCycleProvider()
        {
            OnDispose += () => { };
        }

        public event Action OnDispose;
        public void Dispose() { OnDispose?.Invoke(); }
        public void LogError(ReadOnlySpan<char> message) { }
        public void LogInfo(ReadOnlySpan<char> message) { }
        public void LogSuccess(ReadOnlySpan<char> message) { }
        public void LogWarning(ReadOnlySpan<char> message) { }
        void IAssetSystemHost.PostToMainThread(Action action) { }
    }

    private sealed class TrackingStream : MemoryStream
    {
        public TrackingStream(byte[] buffer) : base(buffer) { }
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private class TestFileSource : IFileSource
    {
        public string Name => "Test";
        public int Priority => 0;
        public IEnumerable<string> AllFileNames => _files.Keys;

        private readonly Dictionary<string, byte[]> _files = new();

        public List<TrackingStream> OpenedStreams { get; } = new();

        public void AddFile(string filename, byte[] content)
            => _files[filename] = content;

        public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, [NotNullWhen(false)] out string? failureReason)
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

        public bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
        {
            if (_files.TryGetValue(path, out var bytes))
            {
                var tracked = new TrackingStream(bytes);
                OpenedStreams.Add(tracked);
                stream = tracked;
                failureReason = null;
                return true;
            }
            stream = null;
            failureReason = $"File not found: {path}";
            return false;
        }

        public void Dispose() => _files.Clear();
    }

    // 32x32 RGBA PNG (the basn6a08 reference image).
    private static readonly byte[] Png32 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAABGdBTUEAAYagMeiWXwAAAG9JREFUeJzt1jEKgDAM" +
        "RuEnZGhPofc/VQSPIcTdxUV4HVLoUCj8H00o2YoBMF57fpz/ujODHXUFRwPKBqj5DVigB041HiJ9gFyCVOMbsEIP" +
        "XNwuAHkgiJL/4qABNqB7QAeUPBAE2QAZUDZAfwEb8ABSIBqcFg+4TAAAAABJRU5ErkJggg==");

    // 8x8 BC1 DDS: the header claims 3 levels, only 8x8 and 4x4 are block-aligned,
    // so the usable chain has 2 levels (32 + 8 = 40 payload bytes).
    private static byte[] CreateDds8x8()
    {
        byte[] data = new byte[128 + 40];
        Span<byte> span = data;
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x20534444);   // "DDS " magic
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 124);     // header size
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], 8);       // height
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 8);       // width
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], 3);       // mip levels
        BinaryPrimitives.WriteUInt32LittleEndian(span[80..], 0x4);    // DDPF_FOURCC
        BinaryPrimitives.WriteUInt32LittleEndian(span[84..], 0x31545844); // "DXT1"
        return data;
    }

    private GPUDevice _device;
    private RenderingSystem _renderingSystem;
    private LifeCycleProvider _lifeCycle;
    private AssetSystem _assetSystem;
    private TestFileSource _fileSource;

    /// <summary>Creates the headless rendering and asset systems for each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU, Headless = true });
        _renderingSystem = new RenderingSystem(_device, textureHeapByteCount: 4 * 1024 * 1024, bufferHeapByteCount: 1 * 1024 * 1024);

        _lifeCycle = new LifeCycleProvider();
        _assetSystem = new AssetSystem(_lifeCycle);
        _fileSource = new TestFileSource();
        _assetSystem.AddFileSource(_fileSource);
        _assetSystem.RegisterAssetLoader(new AssetLoaderTexture2D(_renderingSystem, _assetSystem));
    }

    /// <summary>Disposes the asset system, rendering system, device, and file source.</summary>
    [TearDown]
    public void TearDown()
    {
        _device.WaitIdle();
        _lifeCycle?.Dispose();
        _fileSource?.Dispose();
        _renderingSystem?.Destroy();
        _device?.Destroy();
    }

    /// <summary>Tests that a file-backed PNG is pre-created at load and uploaded in place.</summary>
    [Test]
    public void FileBackedPng_PreCreatedAtLoadThenUploadedInPlace()
    {
        _fileSource.AddFile("textures/wall.png", Png32);

        Texture2D texture = _assetSystem.Load<Texture2D>("textures/wall.png");

        // The specification is final before any content arrived.
        Assert.That(texture.Width, Is.EqualTo(32));
        Assert.That(texture.Height, Is.EqualTo(32));
        Assert.That(texture.MipLevels, Is.EqualTo(1));
        Assert.That(texture.Format, Is.EqualTo(Format.Rgba8Unorm));

        Texture native = texture.NativeTexture;

        // The upload task disposes the asset stream when done.
        Assert.That(_fileSource.OpenedStreams, Has.Count.EqualTo(1));
        Assert.That(SpinWait.SpinUntil(() => _fileSource.OpenedStreams[0].Disposed, TimeSpan.FromSeconds(10)), Is.True);

        // Content arrived in place: the identity never changed.
        Assert.That(texture.NativeTexture, Is.SameAs(native));
        Assert.That(texture.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(texture.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that a DDS on a BC-capable device streams its BC1 mip chain in place.</summary>
    [Test]
    public void FileBackedDds_PreCreatedAtBcSpecThenUploadedInPlace()
    {
        _fileSource.AddFile("textures/compressed.dds", CreateDds8x8());

        Texture2D texture = _assetSystem.Load<Texture2D>("textures/compressed.dds");

        // NoGPUDevice reports BC support; only the block-aligned 8x8 and 4x4 levels survive.
        Assert.That(texture.Width, Is.EqualTo(8));
        Assert.That(texture.Height, Is.EqualTo(8));
        Assert.That(texture.MipLevels, Is.EqualTo(2));
        Assert.That(texture.Format, Is.EqualTo(Format.Bc1Unorm));

        Texture native = texture.NativeTexture;
        Assert.That(_fileSource.OpenedStreams, Has.Count.EqualTo(1));
        Assert.That(SpinWait.SpinUntil(() => _fileSource.OpenedStreams[0].Disposed, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(texture.NativeTexture, Is.SameAs(native));
        Assert.That(texture.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(texture.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that a preloaded context decodes synchronously without opening a stream.</summary>
    [Test]
    public void PreloadedContext_DecodesSynchronously()
    {
        Assert.That(_assetSystem.TryDecode("wall.png", typeof(Texture2D), Png32, out object? asset), Is.True);

        using Texture2D texture = (Texture2D)asset!;
        Assert.That(texture.Width, Is.EqualTo(32));
        Assert.That(texture.Height, Is.EqualTo(32));

        // Preloaded contexts never open a stream.
        Assert.That(_fileSource.OpenedStreams, Is.Empty);
    }

    /// <summary>Tests that a corrupt file fails after the header probe falls back to decoding.</summary>
    [Test]
    public void CorruptFile_ProbeFallsBackToDecodeAndFails()
    {
        _fileSource.AddFile("textures/corrupt.png", new byte[256]);

        Assert.Throws<AssetLoadException>(() => _assetSystem.Load<Texture2D>("textures/corrupt.png"));
    }
}
