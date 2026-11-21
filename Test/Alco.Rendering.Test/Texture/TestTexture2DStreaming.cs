using System.Buffers.Binary;
using System.IO;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// Tests header-driven texture creation and in-place content upload, including streaming, BC fallback,
/// and error paths on the NoGPU device.
/// </summary>
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

    /// <summary>Tests that a PNG header creates an empty texture at the file specification.</summary>
    [Test]
    public void CreateFromHeader_Png_CreatesEmptyAtFileSpec()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        var option = ImageLoadOption.Default with { Format = Format.Rgba8Srgb };

        using Texture2D texture = Texture2DUtility.CreateTexture2DFromHeader(rendering, info, option);

        Assert.That(texture.Width, Is.EqualTo(32));
        Assert.That(texture.Height, Is.EqualTo(32));
        Assert.That(texture.MipLevels, Is.EqualTo(1));
        Assert.That(texture.Format, Is.EqualTo(Format.Rgba8Srgb));
    }

    /// <summary>Tests that DDS without BC support falls back to a single-level RGBA8 texture.</summary>
    [Test]
    public void CreateFromHeader_Dds_WithoutBcSupport_FallsBackToRgba()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        // 8x8 BC1, header claims 3 levels but only 8x8 and 4x4 are block-aligned.
        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        Assert.That(info.MipLevels, Is.EqualTo(2));

        using Texture2D texture = Texture2DUtility.CreateTexture2DFromHeader(rendering, info, option: null, bcSupported: false);

        Assert.That(texture.Width, Is.EqualTo(8));
        Assert.That(texture.Height, Is.EqualTo(8));
        Assert.That(texture.MipLevels, Is.EqualTo(1));
        Assert.That(texture.Format, Is.EqualTo(Format.Rgba8Unorm));
    }

    /// <summary>Tests that DDS with BC support uses the file's BC1 specification and mip count.</summary>
    [Test]
    public void CreateFromHeader_Dds_WithBcSupport_UsesFileSpec()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        // 8x8 BC1, header claims 3 levels but only 8x8 and 4x4 are block-aligned.
        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);

        using Texture2D texture = Texture2DUtility.CreateTexture2DFromHeader(rendering, info, option: null, bcSupported: true);

        Assert.That(texture.Width, Is.EqualTo(8));
        Assert.That(texture.Height, Is.EqualTo(8));
        Assert.That(texture.MipLevels, Is.EqualTo(2));
        Assert.That(texture.Format, Is.EqualTo(Format.Bc1Unorm));
    }

    /// <summary>Tests that uploading PNG content preserves texture identity.</summary>
    [Test]
    public void UploadContent_Png_KeepsIdentity()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Texture2D texture = Texture2DUtility.CreateTexture2DFromHeader(rendering, info);
        Texture native = texture.NativeTexture;

        Assert.DoesNotThrow(() => Texture2DUtility.UploadTexture2DContent(texture, data));

        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    /// <summary>Tests that DDS upload without BC support decodes level 0 and marks content loaded.</summary>
    [Test]
    public void UploadContent_Dds_WithoutBcSupport_DecodesLevel0AndMarksLoaded()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Texture2D texture = Texture2DUtility.CreateTexture2DFromHeader(rendering, info, option: null, bcSupported: false);
        Assert.That(texture.Format, Is.EqualTo(Format.Rgba8Unorm));
        Assert.That(texture.IsContentLoaded, Is.False);
        Texture native = texture.NativeTexture;

        Assert.DoesNotThrow(() => Texture2DUtility.UploadTexture2DContent(texture, data));

        Assert.That(texture.IsContentLoaded, Is.True);
        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    /// <summary>Tests that DDS upload with BC support keeps identity and marks content loaded.</summary>
    [Test]
    public void UploadContent_Dds_WithBcSupport_UploadsMipChainKeepsIdentity()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Texture2D texture = Texture2DUtility.CreateTexture2DFromHeader(rendering, info, option: null, bcSupported: true);
        Assert.That(texture.Format, Is.EqualTo(Format.Bc1Unorm));
        Texture native = texture.NativeTexture;

        Assert.DoesNotThrow(() => Texture2DUtility.UploadTexture2DContent(texture, data));

        Assert.That(texture.IsContentLoaded, Is.True);
        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    /// <summary>Tests that a truncated DDS payload throws.</summary>
    [Test]
    public void UploadContent_Dds_TruncatedPayload_Throws()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        // Header claims 8x8 with mips but the payload is missing entirely.
        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 0);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Texture2D texture = Texture2DUtility.CreateTexture2DFromHeader(rendering, info);

        Assert.Throws<ImageDecodeException>(() => Texture2DUtility.UploadTexture2DContent(texture, data));
    }

    /// <summary>Tests that uploading content that does not match the texture specification throws and preserves identity.</summary>
    [Test]
    public void UploadContent_SpecMismatch_Throws()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png"); // 32x32
        using Texture2D texture = Texture2DUtility.CreateTexture2D(rendering, 16, 16);
        Texture native = texture.NativeTexture;

        Assert.Throws<ImageDecodeException>(() => Texture2DUtility.UploadTexture2DContent(texture, data));

        Assert.That(texture.NativeTexture, Is.SameAs(native));
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

    /// <summary>Tests that streaming pre-creates the texture, uploads in place, and disposes the stream.</summary>
    [Test]
    public void CreateStreaming_Png_PreCreatesThenUploadsInPlace()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        var stream = new TrackingStream(data);
        using Texture2D texture = Texture2DUtility.CreateTexture2DStreaming(rendering, stream);

        Assert.That(texture.Width, Is.EqualTo(32));
        Assert.That(texture.Height, Is.EqualTo(32));
        Assert.That(texture.MipLevels, Is.EqualTo(1));
        Texture native = texture.NativeTexture;

        Assert.That(SpinWait.SpinUntil(() => stream.Disposed, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(texture.NativeTexture, Is.SameAs(native));
    }

    /// <summary>Tests that a corrupt streaming header throws and leaves the stream to the caller.</summary>
    [Test]
    public void CreateStreaming_CorruptHeader_ThrowsAndCallerKeepsStream()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        var stream = new TrackingStream(new byte[256]);
        Assert.Throws<ImageDecodeException>(() => Texture2DUtility.CreateTexture2DStreaming(rendering, stream));

        Assert.That(stream.Disposed, Is.False);
        Assert.DoesNotThrow(() => stream.Dispose());
    }

    /// <summary>Tests that a header-created texture reports content loaded only after upload.</summary>
    [Test]
    public void CreateFromHeader_Png_ContentNotLoadedUntilUpload()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        using Texture2D texture = Texture2DUtility.CreateTexture2DFromHeader(rendering, info);

        Assert.That(texture.IsContentLoaded, Is.False);
        Assert.That(texture.ContentArrival.IsCompleted, Is.True);

        Texture2DUtility.UploadTexture2DContent(texture, data);
        Assert.That(texture.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that a texture created with content reports content loaded immediately.</summary>
    [Test]
    public void CreateFromColor_ReportsContentLoadedByDefault()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        using Texture2D texture = Texture2DUtility.CreateTexture2D(rendering, 4, 4, Color32.White);

        Assert.That(texture.IsContentLoaded, Is.True);
        Assert.That(texture.ContentArrival.IsCompleted, Is.True);
    }

    /// <summary>Tests that streaming content arrival completes and marks content loaded.</summary>
    [Test]
    public void CreateStreaming_Png_ContentArrivalCompletesAndMarksContentLoaded()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        using Texture2D texture = Texture2DUtility.CreateTexture2DStreaming(rendering, new MemoryStream(data));

        Assert.That(texture.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(texture.ContentArrival.IsCompletedSuccessfully, Is.True);
        Assert.That(texture.IsContentLoaded, Is.True);
    }
}
