using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.IO;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>Covers <see cref="Image"/> asset streaming, including header probing, in-place uploads, and decode fallbacks.</summary>
[TestFixture]
public class TestAssetLoaderImageStreaming
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
        void IAssetSystemHost.PostToMainThread(Action action) => action();
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
    private ImageUploadQueue _imageUploadQueue;
    private LifeCycleProvider _lifeCycle;
    private AssetSystem _assetSystem;
    private TestFileSource _fileSource;

    /// <summary>Creates the headless rendering and asset systems for each test.</summary>
    [SetUp]
    public void SetUp()
    {
        _device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU, Headless = true });
        _renderingSystem = new RenderingSystem(_device, textureHeapByteCount: 4 * 1024 * 1024, bufferHeapByteCount: 1 * 1024 * 1024);
        _imageUploadQueue = new ImageUploadQueue(_renderingSystem);

        _lifeCycle = new LifeCycleProvider();
        _assetSystem = new AssetSystem(_lifeCycle);
        _fileSource = new TestFileSource();
        _assetSystem.AddFileSource(_fileSource);
        _assetSystem.RegisterAssetLoader(new AssetLoaderImage(_imageUploadQueue, _assetSystem));
        _assetSystem.RegisterAssetLoader(new AssetLoaderMeta(new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter(), new JsonConverterPadding() },
        }));
    }

    /// <summary>Disposes the asset system, rendering system, device, and file source.</summary>
    [TearDown]
    public void TearDown()
    {
        _device.WaitIdle();
        _imageUploadQueue?.Dispose();
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

        Image image = _assetSystem.Load<Image>("textures/wall.png");

        // The specification is final before any content arrived.
        Assert.That(image.Width, Is.EqualTo(32));
        Assert.That(image.Height, Is.EqualTo(32));
        Assert.That(image.MipLevels, Is.EqualTo(1));
        Assert.That(image.Format, Is.EqualTo(Format.Rgba8Unorm));

        Texture gpuTexture = image.Texture;

        // The upload task disposes the asset stream when done.
        Assert.That(_fileSource.OpenedStreams, Has.Count.EqualTo(1));
        Assert.That(SpinWait.SpinUntil(() => _fileSource.OpenedStreams[0].Disposed, TimeSpan.FromSeconds(10)), Is.True);

        // Content arrived in place: the identity never changed.
        PumpUntilArrived(image);
        Assert.That(image.Texture, Is.SameAs(gpuTexture));
        Assert.That(image.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that a DDS on a BC-capable device streams its BC1 mip chain in place.</summary>
    [Test]
    public void FileBackedDds_PreCreatedAtBcSpecThenUploadedInPlace()
    {
        _fileSource.AddFile("textures/compressed.dds", CreateDds8x8());

        Image image = _assetSystem.Load<Image>("textures/compressed.dds");

        // NoGPUDevice reports BC support; only the block-aligned 8x8 and 4x4 levels survive.
        Assert.That(image.Width, Is.EqualTo(8));
        Assert.That(image.Height, Is.EqualTo(8));
        Assert.That(image.MipLevels, Is.EqualTo(2));
        Assert.That(image.Format, Is.EqualTo(Format.Bc1Unorm));

        Texture gpuTexture = image.Texture;
        Assert.That(_fileSource.OpenedStreams, Has.Count.EqualTo(1));
        Assert.That(SpinWait.SpinUntil(() => _fileSource.OpenedStreams[0].Disposed, TimeSpan.FromSeconds(10)), Is.True);
        PumpUntilArrived(image);
        Assert.That(image.Texture, Is.SameAs(gpuTexture));
        Assert.That(image.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that a preloaded context decodes synchronously without opening a stream.</summary>
    [Test]
    public void PreloadedContext_DecodesSynchronously()
    {
        Assert.That(_assetSystem.TryDecode("wall.png", typeof(Image), Png32, out object? asset), Is.True);

        using Image image = (Image)asset!;
        Assert.That(image.Width, Is.EqualTo(32));
        Assert.That(image.Height, Is.EqualTo(32));

        // Preloaded contexts never open a stream.
        Assert.That(_fileSource.OpenedStreams, Is.Empty);
    }

    /// <summary>Tests that a corrupt file fails after the header probe falls back to decoding.</summary>
    [Test]
    public void CorruptFile_ProbeFallsBackToDecodeAndFails()
    {
        _fileSource.AddFile("textures/corrupt.png", new byte[256]);

        Assert.Throws<AssetLoadException>(() => _assetSystem.Load<Image>("textures/corrupt.png"));
    }

    /// <summary>
    /// Tests that <see cref="AssetLoaderImage"/> resolves directory options and per-file <see cref="ImageMeta"/>
    /// at load time and hot-reloads into a fresh immutable <see cref="Image"/> without disposing the prior asset.
    /// </summary>
    [Test]
    public void MetaAndHotReload_ResolvesOptionsAndReplacesImmutableImageSafely()
    {
        _fileSource.AddFile("textures/.texture-option.meta", System.Text.Encoding.UTF8.GetBytes("""
            {
                "FilterMode": "Nearest",
                "AddressMode": "ClampToEdge",
                "Anisotropy": 4,
                "PremultiplyAlpha": true
            }
            """));
        _fileSource.AddFile("textures/wall.png", Png32);
        _fileSource.AddFile("textures/wall.png.meta", System.Text.Encoding.UTF8.GetBytes("""
            {
                "SlicePadding": { "Left": 2, "Top": 4, "Right": 6, "Bottom": 8 },
                "Sprites": {
                    "tile": { "X": 0, "Y": 0, "Width": 16, "Height": 16 }
                }
            }
            """));

        Image first = _assetSystem.Load<Image>("textures/wall.png");
        Assert.That(first.FilterMode, Is.EqualTo(Filter.Nearest));
        Assert.That(first.AddressMode, Is.EqualTo(AddressMode.ClampToEdge));
        Assert.That(first.Anisotropy, Is.EqualTo(4));
        Assert.That(first.IsPremultiplied, Is.True);
        Assert.That(first.SlicePadding, Is.EqualTo(new Padding(2, 4, 6, 8)));
        Assert.That(first.TryGetSprite("tile", out Sprite? tileSprite), Is.True);
        Assert.That(tileSprite!.UvRect, Is.EqualTo(new Rect(0, 0, 0.5f, 0.5f)));
        PumpUntilArrived(first);

        using ManualResetEventSlim reloadedEvent = new(false);
        object? eventAsset = null;
        _assetSystem.OnHotReload += (_, asset) =>
        {
            eventAsset = asset;
            reloadedEvent.Set();
        };

        _assetSystem.EnqueueHotReload("textures/wall.png");
        Assert.That(reloadedEvent.Wait(TimeSpan.FromSeconds(10)), Is.True);

        Image second = _assetSystem.Load<Image>("textures/wall.png");
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second, Is.SameAs(eventAsset));
        PumpUntilArrived(second);
        Assert.That(first.IsDisposed, Is.False);
        Assert.That(first.Texture.IsDestroyed, Is.False);
        first.Dispose();
    }

    private void PumpUntilArrived(Image image)
    {
        bool completed = SpinWait.SpinUntil(() =>
        {
            _imageUploadQueue.Pump();
            return image.ContentArrival.IsCompleted;
        }, TimeSpan.FromSeconds(10));
        Assert.That(completed, Is.True, $"Timed out waiting for '{image.Name}' streaming upload.");
    }
}
