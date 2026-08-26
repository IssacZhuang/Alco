using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Engine.Test;

// ─────────────────────────────────────────────────────────────────────────────
// glTF model textures: the loader realizes every referenced image before the
// scene returns — external files stream their content in place (header probe +
// asynchronous in-place upload), embedded images decode synchronously. Texture
// identities and material bindings are therefore final at load; tests observe
// the streaming lifecycle through the disposal of the asset stream. Runs on the
// NoGPU device with an in-memory file source.
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class TestModelGltfTextures
{
    /// <summary>Minimal <see cref="IRenderingSystemHost"/> for a headless rendering system.</summary>
    private class RenderingHost : IRenderingSystemHost
    {
        public event Action<float> OnUpdate;
        public event Action OnDispose;
        public void Dispose() { OnDispose?.Invoke(); }
    }

    /// <summary>Minimal <see cref="IAssetSystemHost"/> satisfying the constructor contract.</summary>
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

    /// <summary>A stream that records disposal — the streaming contract disposes the
    /// stream when the upload task finishes, which is how tests observe completion.</summary>
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

    /// <summary>In-memory <see cref="IFileSource"/> for injecting test files.</summary>
    private class TestFileSource : IFileSource
    {
        public string Name => "Test";
        public int Priority => 0;
        public IEnumerable<string> AllFileNames => _files.Keys;

        private readonly Dictionary<string, byte[]> _files = new();

        /// <summary>Streams handed out so far, for observing the streaming lifecycle.</summary>
        public List<TrackingStream> OpenedStreams { get; } = new();

        public void AddFile(string filename, byte[] content)
            => _files[filename] = content;

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

    /// <summary>
    /// A minimal one-triangle glTF scene whose single material has one texture slot
    /// referencing images[0]. Vertex data rides a data-URI buffer.
    /// </summary>
    private static byte[] CreateGltf(string imageUri, string textureSlot)
    {
        // Three all-zero positions (36 bytes); normals are generated by the decoder.
        string positionsBase64 = Convert.ToBase64String(new byte[36]);
        string materialTexture = textureSlot == "baseColorTexture"
            ? "\"pbrMetallicRoughness\": { \"baseColorTexture\": { \"index\": 0 } }"
            : $"\"pbrMetallicRoughness\": {{}}, \"{textureSlot}\": {{ \"index\": 0 }}";
        const string template = """
        {
            "asset": { "version": "2.0" },
            "scene": 0,
            "scenes": [ { "nodes": [0] } ],
            "nodes": [ { "mesh": 0 } ],
            "meshes": [ { "name": "tri", "primitives": [ { "attributes": { "POSITION": 0 }, "material": 0 } ] } ],
            "materials": [ { "name": "mat", @MATERIAL_TEXTURE@ } ],
            "textures": [ { "source": 0 } ],
            "images": [ { "uri": "@IMAGE_URI@", "name": "tex" } ],
            "accessors": [ { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" } ],
            "bufferViews": [ { "buffer": 0, "byteOffset": 0, "byteLength": 36 } ],
            "buffers": [ { "byteLength": 36, "uri": "data:application/octet-stream;base64,@POSITIONS@" } ]
        }
        """;
        return Encoding.UTF8.GetBytes(template
            .Replace("@MATERIAL_TEXTURE@", materialTexture)
            .Replace("@IMAGE_URI@", imageUri)
            .Replace("@POSITIONS@", positionsBase64));
    }

    private RenderingHost _renderingHost;
    private RenderingSystem _renderingSystem;
    private LifeCycleProvider _lifeCycle;
    private AssetSystem _assetSystem;
    private TestFileSource _fileSource;

    [SetUp]
    public void SetUp()
    {
        _renderingHost = new RenderingHost();
        _renderingSystem = new RenderingSystem(
            _renderingHost,
            GraphicsDeviceFactory.GetNoGPUDevice(),
            PixelFormat.RGBA16Float,
            PixelFormat.Depth24PlusStencil8);

        _lifeCycle = new LifeCycleProvider();
        _assetSystem = new AssetSystem(_lifeCycle);
        _fileSource = new TestFileSource();
        _assetSystem.AddFileSource(_fileSource);
        _assetSystem.RegisterAssetLoader(new AssetLoaderModelGltf(_renderingSystem));
    }

    [TearDown]
    public void TearDown()
    {
        _renderingHost?.Dispose();
        _lifeCycle?.Dispose();
        _fileSource?.Dispose();
    }

    private ModelScene LoadScene(string gltf, string textureSlot = "baseColorTexture")
    {
        _fileSource.AddFile("models/scene.gltf", CreateGltf(gltf, textureSlot));
        return _assetSystem.Load<ModelScene>("models/scene.gltf");
    }

    [Test]
    public void ExternalPng_AlbedoIsFinalAtLoadAndSrgb()
    {
        _fileSource.AddFile("models/textures/albedo.png", Png32);

        ModelScene scene = LoadScene("textures/albedo.png");

        // The binding is final the moment the scene returns.
        Texture2D? texture = scene.Materials[0].AlbedoTexture;
        Assert.That(texture, Is.Not.Null);
        Assert.That(texture!.Width, Is.EqualTo(32));
        Assert.That(texture.Height, Is.EqualTo(32));
        Assert.That(texture.MipLevels, Is.EqualTo(1));
        // Albedo is color data: sRGB.
        Assert.That(texture.NativeTexture.PixelFormat, Is.EqualTo(PixelFormat.RGBA8UnormSrgb));

        GPUTexture native = texture.NativeTexture;
        Assert.That(_fileSource.OpenedStreams, Has.Count.EqualTo(1));
        Assert.That(SpinWait.SpinUntil(() => _fileSource.OpenedStreams[0].Disposed, TimeSpan.FromSeconds(10)), Is.True);

        // The content streamed in place: the identity never changed.
        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    [Test]
    public void ExternalPng_NormalMapStaysLinear()
    {
        _fileSource.AddFile("models/textures/normal.png", Png32);

        ModelScene scene = LoadScene("textures/normal.png", "normalTexture");

        Texture2D? texture = scene.Materials[0].NormalTexture;
        Assert.That(texture, Is.Not.Null);
        // Normal maps are linear data, never sRGB.
        Assert.That(texture!.NativeTexture.PixelFormat, Is.EqualTo(PixelFormat.RGBA8Unorm));
    }

    [Test]
    public void ExternalDds_StreamsMipChainWithSrgbFormat()
    {
        _fileSource.AddFile("models/textures/albedo.dds", CreateDds8x8());

        ModelScene scene = LoadScene("textures/albedo.dds");

        Texture2D? texture = scene.Materials[0].AlbedoTexture;
        Assert.That(texture, Is.Not.Null);
        Assert.That(texture!.Width, Is.EqualTo(8));
        Assert.That(texture.Height, Is.EqualTo(8));
        Assert.That(texture.MipLevels, Is.EqualTo(2));
        // Block-compressed files take the header's format; the albedo role picks the sRGB variant.
        Assert.That(texture.NativeTexture.PixelFormat, Is.EqualTo(PixelFormat.BC1RGBAUnormSrgb));

        Assert.That(_fileSource.OpenedStreams, Has.Count.EqualTo(1));
        Assert.That(SpinWait.SpinUntil(() => _fileSource.OpenedStreams[0].Disposed, TimeSpan.FromSeconds(10)), Is.True);
    }

    [Test]
    public void MissingImage_KeepsTheFallback()
    {
        ModelScene scene = LoadScene("textures/missing.png");

        // Tolerated: the material keeps its null texture (fallback binding).
        Assert.That(scene.Materials[0].AlbedoTexture, Is.Null);
        Assert.That(_fileSource.OpenedStreams, Is.Empty);
    }

    [Test]
    public void CorruptImage_KeepsTheFallback()
    {
        _fileSource.AddFile("models/textures/corrupt.png", new byte[256]);

        ModelScene scene = LoadScene("textures/corrupt.png");

        // Probe failed, the synchronous fallback decode failed too: tolerated.
        Assert.That(scene.Materials[0].AlbedoTexture, Is.Null);
    }

    [Test]
    public void EmbeddedDataUriPng_DecodesSynchronously()
    {
        string dataUri = "data:image/png;base64," + Convert.ToBase64String(Png32);

        ModelScene scene = LoadScene(dataUri);

        Texture2D? texture = scene.Materials[0].AlbedoTexture;
        Assert.That(texture, Is.Not.Null);
        Assert.That(texture!.Width, Is.EqualTo(32));
        Assert.That(texture.NativeTexture.PixelFormat, Is.EqualTo(PixelFormat.RGBA8UnormSrgb));

        // Embedded bytes are already in memory: no stream was opened.
        Assert.That(_fileSource.OpenedStreams, Is.Empty);
    }
}
