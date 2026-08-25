using System.Buffers.Binary;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

// ─────────────────────────────────────────────────────────────────────────────
// Texture streaming primitives (plan A): CreateTexture2DFromHeader pre-creates the
// texture at the file-dictated specification and UploadTexture2DContent uploads the
// decoded content in place, so the texture's identity never changes. Runs on the
// NoGPU device: specification and identity are verifiable, pixel content is not
// (NoTexture discards writes).
// ─────────────────────────────────────────────────────────────────────────────
[TestFixture]
public class TestTexture2DStreaming
{
    private const uint FourCcDxt1 = 0x31545844; // "DXT1"

    private static byte[] LoadTestFile(string subfolder, string filename)
        => File.ReadAllBytes(Path.Combine("Files", "Image", subfolder, filename));

    private static byte[] CreateDdsBytes(int width, int height, int mipLevels, int payloadBytes)
    {
        byte[] data = new byte[128 + payloadBytes];
        Span<byte> span = data;
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x20534444);   // "DDS " magic
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 124);     // header size
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], height);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], mipLevels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[80..], 0x4);    // DDPF_FOURCC
        BinaryPrimitives.WriteUInt32LittleEndian(span[84..], FourCcDxt1);
        return data;
    }

    [Test]
    public void CreateFromHeader_Png_CreatesEmptyAtFileSpec()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        var option = ImageLoadOption.Default with { Format = PixelFormat.RGBA8UnormSrgb };

        using Texture2D texture = rendering.CreateTexture2DFromHeader(info, option);

        Assert.That(texture.Width, Is.EqualTo(32));
        Assert.That(texture.Height, Is.EqualTo(32));
        Assert.That(texture.MipLevels, Is.EqualTo(1));
        Assert.That(texture.NativeTexture.PixelFormat, Is.EqualTo(PixelFormat.RGBA8UnormSrgb));
    }

    [Test]
    public void CreateFromHeader_Dds_UsesFileSpecOverOption()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        // 8x8 BC1, header claims 3 levels but only 8x8 and 4x4 are block-aligned.
        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        Assert.That(info.MipLevels, Is.EqualTo(2));

        using Texture2D texture = rendering.CreateTexture2DFromHeader(info);

        Assert.That(texture.Width, Is.EqualTo(8));
        Assert.That(texture.Height, Is.EqualTo(8));
        Assert.That(texture.MipLevels, Is.EqualTo(2));
        Assert.That(texture.NativeTexture.PixelFormat, Is.EqualTo(PixelFormat.BC1RGBAUnorm));
    }

    [Test]
    public void UploadContent_Png_KeepsIdentity()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Texture2D texture = rendering.CreateTexture2DFromHeader(info);
        GPUTexture native = texture.NativeTexture;

        Assert.DoesNotThrow(() => rendering.UploadTexture2DContent(texture, data));

        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    [Test]
    public void UploadContent_Dds_UploadsMipChainKeepsIdentity()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Texture2D texture = rendering.CreateTexture2DFromHeader(info);
        GPUTexture native = texture.NativeTexture;

        Assert.DoesNotThrow(() => rendering.UploadTexture2DContent(texture, data));

        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    [Test]
    public void UploadContent_Dds_TruncatedPayload_Throws()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        // Header claims 8x8 with mips but the payload is missing entirely.
        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 0);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Texture2D texture = rendering.CreateTexture2DFromHeader(info);

        Assert.Throws<ImageDecodeException>(() => rendering.UploadTexture2DContent(texture, data));
    }

    [Test]
    public void UploadContent_SpecMismatch_Throws()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png"); // 32x32
        using Texture2D texture = rendering.CreateTexture2D(16, 16);
        GPUTexture native = texture.NativeTexture;

        Assert.Throws<ImageDecodeException>(() => rendering.UploadTexture2DContent(texture, data));

        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    /// <summary>
    /// A read-only stream over bytes that records disposal — the streaming contract
    /// disposes the stream when the upload task finishes, which is how tests observe
    /// completion without any state on the texture.
    /// </summary>
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

    [Test]
    public void CreateStreaming_Png_PreCreatesThenUploadsInPlace()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        var stream = new TrackingStream(data);
        using Texture2D texture = rendering.CreateTexture2DStreaming(stream);

        // The specification is final before any content arrived.
        Assert.That(texture.Width, Is.EqualTo(32));
        Assert.That(texture.Height, Is.EqualTo(32));
        Assert.That(texture.MipLevels, Is.EqualTo(1));
        GPUTexture native = texture.NativeTexture;

        // The upload task disposes the stream when done.
        Assert.That(SpinWait.SpinUntil(() => stream.Disposed, TimeSpan.FromSeconds(10)), Is.True);

        // Content arrived in place: the identity never changed.
        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    [Test]
    public void CreateStreaming_CorruptHeader_ThrowsAndCallerKeepsStream()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        var stream = new TrackingStream(new byte[256]);
        Assert.Throws<ImageDecodeException>(() => rendering.CreateTexture2DStreaming(stream));

        // Probe failure left the stream ownership with the caller.
        Assert.That(stream.Disposed, Is.False);
        Assert.DoesNotThrow(() => stream.Dispose());
    }
}
