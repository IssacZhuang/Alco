using System.Buffers.Binary;
using System.IO;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Tests header-driven <see cref="Image"/> creation and in-place content upload, including streaming,
/// BC fallback, and error paths on the NoGPU device.
/// </summary>
[TestFixture]
public class TestImageStreaming
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

    /// <summary>Tests that a PNG header creates an empty image at the file specification.</summary>
    [Test]
    public void CreateFromHeader_Png_CreatesEmptyAtFileSpec()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        var option = ImageLoadOption.Default with { Format = Format.Rgba8Srgb };

        using Image image = ImageUtility.CreateFromHeader(rendering, info, option);

        Assert.That(image.Width, Is.EqualTo(32));
        Assert.That(image.Height, Is.EqualTo(32));
        Assert.That(image.MipLevels, Is.EqualTo(1));
        Assert.That(image.Format, Is.EqualTo(Format.Rgba8Srgb));
    }

    /// <summary>Tests that DDS without BC support falls back to a single-level RGBA8 texture.</summary>
    [Test]
    public void CreateFromHeader_Dds_WithoutBcSupport_FallsBackToRgba()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        Assert.That(info.MipLevels, Is.EqualTo(2));

        using Image image = ImageUtility.CreateFromHeader(rendering, info, option: null, sprites: null, bcSupported: false);

        Assert.That(image.Width, Is.EqualTo(8));
        Assert.That(image.Height, Is.EqualTo(8));
        Assert.That(image.MipLevels, Is.EqualTo(1));
        Assert.That(image.Format, Is.EqualTo(Format.Rgba8Unorm));
    }

    /// <summary>Tests that DDS with BC support uses the file's BC1 specification and mip count.</summary>
    [Test]
    public void CreateFromHeader_Dds_WithBcSupport_UsesFileSpec()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);

        using Image image = ImageUtility.CreateFromHeader(rendering, info, option: null, sprites: null, bcSupported: true);

        Assert.That(image.Width, Is.EqualTo(8));
        Assert.That(image.Height, Is.EqualTo(8));
        Assert.That(image.MipLevels, Is.EqualTo(2));
        Assert.That(image.Format, Is.EqualTo(Format.Bc1Unorm));
    }

    /// <summary>Tests that uploading PNG content preserves texture identity.</summary>
    [Test]
    public void UploadContent_Png_KeepsIdentity()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Image image = ImageUtility.CreateFromHeader(rendering, info);
        Texture gpuTexture = image.Texture;

        Assert.DoesNotThrow(() => ImageUtility.UploadContent(rendering, image, data));

        Assert.That(image.Texture, Is.SameAs(gpuTexture));
    }

    /// <summary>Tests that DDS upload without BC support decodes level 0 and marks content loaded.</summary>
    [Test]
    public void UploadContent_Dds_WithoutBcSupport_DecodesLevel0AndMarksLoaded()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Image image = ImageUtility.CreateFromHeader(rendering, info, option: null, sprites: null, bcSupported: false);
        Assert.That(image.Format, Is.EqualTo(Format.Rgba8Unorm));
        Assert.That(image.IsContentLoaded, Is.False);
        Texture gpuTexture = image.Texture;

        Assert.DoesNotThrow(() => ImageUtility.UploadContent(rendering, image, data));

        Assert.That(image.IsContentLoaded, Is.True);
        Assert.That(image.Texture, Is.SameAs(gpuTexture));
    }

    /// <summary>Tests that DDS upload with BC support keeps identity and marks content loaded.</summary>
    [Test]
    public void UploadContent_Dds_WithBcSupport_UploadsMipChainKeepsIdentity()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Image image = ImageUtility.CreateFromHeader(rendering, info, option: null, sprites: null, bcSupported: true);
        Assert.That(image.Format, Is.EqualTo(Format.Bc1Unorm));
        Texture gpuTexture = image.Texture;

        Assert.DoesNotThrow(() => ImageUtility.UploadContent(rendering, image, data));

        Assert.That(image.IsContentLoaded, Is.True);
        Assert.That(image.Texture, Is.SameAs(gpuTexture));
    }

    /// <summary>Tests that a truncated DDS payload throws.</summary>
    [Test]
    public void UploadContent_Dds_TruncatedPayload_Throws()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 0);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Image image = ImageUtility.CreateFromHeader(rendering, info);

        Assert.Throws<ImageDecodeException>(() => ImageUtility.UploadContent(rendering, image, data));
    }

    /// <summary>Tests that uploading content that does not match the image specification throws and preserves identity.</summary>
    [Test]
    public void UploadContent_SpecMismatch_Throws()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png"); // 32x32
        Texture raw = TextureUtility.CreateTexture(rendering, 16, 16);
        using Image image = ImageUtility.CreateFromTexture(raw, ownsResource: true);
        Texture gpuTexture = image.Texture;

        Assert.Throws<ImageDecodeException>(() => ImageUtility.UploadContent(rendering, image, data));

        Assert.That(image.Texture, Is.SameAs(gpuTexture));
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

    /// <summary>Tests that streaming pre-creates the image, uploads in place, and disposes the stream.</summary>
    [Test]
    public void CreateStreaming_Png_PreCreatesThenUploadsInPlace()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        var stream = new TrackingStream(data);
        using Image image = ImageUtility.CreateStreaming(rendering, stream);

        Assert.That(image.Width, Is.EqualTo(32));
        Assert.That(image.Height, Is.EqualTo(32));
        Assert.That(image.MipLevels, Is.EqualTo(1));
        Texture gpuTexture = image.Texture;

        Assert.That(SpinWait.SpinUntil(() => stream.Disposed, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.Texture, Is.SameAs(gpuTexture));
    }

    /// <summary>Tests that a corrupt streaming header throws and leaves the stream to the caller.</summary>
    [Test]
    public void CreateStreaming_CorruptHeader_ThrowsAndCallerKeepsStream()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        var stream = new TrackingStream(new byte[256]);
        Assert.Throws<ImageDecodeException>(() => ImageUtility.CreateStreaming(rendering, stream));

        Assert.That(stream.Disposed, Is.False);
        Assert.DoesNotThrow(() => stream.Dispose());
    }

    /// <summary>Tests that a header-created image reports content loaded only after upload.</summary>
    [Test]
    public void CreateFromHeader_Png_ContentNotLoadedUntilUpload()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Image image = ImageUtility.CreateFromHeader(rendering, info);

        Assert.That(image.IsContentLoaded, Is.False);
        Assert.That(image.ContentArrival.IsCompleted, Is.True);

        ImageUtility.UploadContent(rendering, image, data);
        Assert.That(image.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that an image created over a ready texture reports content loaded immediately.</summary>
    [Test]
    public void CreateFromTexture_ReportsContentLoadedByDefault()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        Texture raw = TextureUtility.CreateTexture(rendering, 4, 4, Color32.White);
        using Image image = ImageUtility.CreateFromTexture(raw, ownsResource: true);

        Assert.That(image.IsContentLoaded, Is.True);
        Assert.That(image.ContentArrival.IsCompleted, Is.True);
    }

    /// <summary>Tests that streaming content arrival completes and marks content loaded.</summary>
    [Test]
    public void CreateStreaming_Png_ContentArrivalCompletesAndMarksContentLoaded()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        using Image image = ImageUtility.CreateStreaming(rendering, new MemoryStream(data));

        Assert.That(image.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.ContentArrival.IsCompletedSuccessfully, Is.True);
        Assert.That(image.IsContentLoaded, Is.True);
    }
}
