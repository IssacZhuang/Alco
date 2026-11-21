using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
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

    /// <summary>Tests that streaming pre-creates the image, uploads in place after pumping, and disposes the stream.</summary>
    [Test]
    public void CreateStreaming_Png_PreCreatesThenUploadsInPlace()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;
        using ImageUploadQueue uploadQueue = new(rendering);

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        var stream = new TrackingStream(data);
        using Image image = ImageUtility.CreateStreaming(uploadQueue, stream);

        Assert.That(image.Width, Is.EqualTo(32));
        Assert.That(image.Height, Is.EqualTo(32));
        Assert.That(image.MipLevels, Is.EqualTo(1));
        Texture gpuTexture = image.Texture;

        Assert.That(SpinWait.SpinUntil(() => stream.Disposed, TimeSpan.FromSeconds(10)), Is.True);
        PumpUntilArrived(uploadQueue, image);
        Assert.That(image.Texture, Is.SameAs(gpuTexture));
        Assert.That(image.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that a corrupt streaming header throws and leaves the stream to the caller.</summary>
    [Test]
    public void CreateStreaming_CorruptHeader_ThrowsAndCallerKeepsStream()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;
        using ImageUploadQueue uploadQueue = new(rendering);

        var stream = new TrackingStream(new byte[256]);
        Assert.Throws<ImageDecodeException>(() => ImageUtility.CreateStreaming(uploadQueue, stream));

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

    /// <summary>Tests that streaming content arrival completes and marks content loaded when pumped.</summary>
    [Test]
    public void CreateStreaming_Png_ContentArrivalCompletesAndMarksContentLoaded()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        RenderingSystem rendering = host.RenderingSystem;
        using ImageUploadQueue uploadQueue = new(rendering);

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        using Image image = ImageUtility.CreateStreaming(uploadQueue, new MemoryStream(data));

        PumpUntilArrived(uploadQueue, image);
        Assert.That(image.ContentArrival.IsCompletedSuccessfully, Is.True);
        Assert.That(image.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that background CPU decoding finishes into the pending queue without uploading until pumped.</summary>
    [Test]
    public void CreateStreaming_DecodeCompletesBeforePump_StaysUnloadedUntilPumped()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ImageUploadQueue uploadQueue = new(host.RenderingSystem);

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        using Image image = ImageUtility.CreateStreaming(uploadQueue, new MemoryStream(data));

        Assert.That(SpinWait.SpinUntil(() => uploadQueue.PendingUploadCount == 1, TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.IsContentLoaded, Is.False);
        Assert.That(image.ContentArrival.IsCompleted, Is.False);

        ulong uploaded = uploadQueue.Pump();
        Assert.That(uploaded, Is.EqualTo(32ul * 32ul * 4ul));
        Assert.That(image.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.ContentArrival.IsCompletedSuccessfully, Is.True);
        Assert.That(image.IsContentLoaded, Is.True);
        Assert.That(uploadQueue.HasPendingWork, Is.False);
    }

    /// <summary>Tests that an RGBA8 image streams in row slices across multiple Pump calls under a small byte budget.</summary>
    [Test]
    public void CreateStreaming_SmallByteBudget_UploadsRgbaAcrossMultiplePumps()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ImageUploadQueue uploadQueue = new(host.RenderingSystem);

        byte[] data = LoadTestFile("Png", "basn6a08.png"); // 32x32 RGBA8 = 4096 bytes
        using Image image = ImageUtility.CreateStreaming(uploadQueue, new MemoryStream(data));
        Assert.That(SpinWait.SpinUntil(() => uploadQueue.PendingUploadCount == 1, TimeSpan.FromSeconds(10)), Is.True);

        for (int step = 0; step < 3; step++)
        {
            ulong stepBytes = uploadQueue.Pump(byteBudget: 1024);
            Assert.That(stepBytes, Is.EqualTo(1024ul));
            Assert.That(image.IsContentLoaded, Is.False);
            Assert.That(image.ContentArrival.IsCompleted, Is.False);
        }

        ulong finalBytes = uploadQueue.Pump(byteBudget: 1024);
        Assert.That(finalBytes, Is.EqualTo(1024ul));
        Assert.That(image.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.ContentArrival.IsCompletedSuccessfully, Is.True);
        Assert.That(image.IsContentLoaded, Is.True);
        Assert.That(uploadQueue.PendingUploadCount, Is.Zero);
    }

    /// <summary>Tests that a BC DDS mip chain uploads across block-row and mip boundaries under a small byte budget.</summary>
    [Test]
    public void CreateStreaming_SmallByteBudget_UploadsDdsMipChainAcrossMultiplePumps()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ImageUploadQueue uploadQueue = new(host.RenderingSystem);

        byte[] data = CreateDdsBytes(8, 8, 3, payloadBytes: 40); // Mip 0: 32 B (2x16 B rows), Mip 1: 8 B
        using Image image = ImageUtility.CreateStreaming(uploadQueue, new MemoryStream(data));
        Assert.That(SpinWait.SpinUntil(() => uploadQueue.PendingUploadCount == 1, TimeSpan.FromSeconds(10)), Is.True);

        Assert.That(uploadQueue.Pump(byteBudget: 16), Is.EqualTo(16ul));
        Assert.That(image.IsContentLoaded, Is.False);
        Assert.That(uploadQueue.Pump(byteBudget: 16), Is.EqualTo(16ul));
        Assert.That(image.IsContentLoaded, Is.False);
        Assert.That(uploadQueue.Pump(byteBudget: 16), Is.EqualTo(8ul));

        Assert.That(image.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.ContentArrival.IsCompletedSuccessfully, Is.True);
        Assert.That(image.IsContentLoaded, Is.True);
        Assert.That(uploadQueue.PendingUploadCount, Is.Zero);
    }

    /// <summary>Tests that a byte budget smaller than one row still makes forward progress by uploading one row.</summary>
    [Test]
    public void CreateStreaming_BudgetSmallerThanOneRow_StillMakesProgress()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ImageUploadQueue uploadQueue = new(host.RenderingSystem);

        byte[] data = LoadTestFile("Png", "basn6a08.png"); // 32x32 RGBA8 -> 128 B per row
        using Image image = ImageUtility.CreateStreaming(uploadQueue, new MemoryStream(data));
        Assert.That(SpinWait.SpinUntil(() => uploadQueue.PendingUploadCount == 1, TimeSpan.FromSeconds(10)), Is.True);

        ulong firstBytes = uploadQueue.Pump(byteBudget: 1);
        Assert.That(firstBytes, Is.EqualTo(128ul));

        int pumps = 1;
        while (!image.ContentArrival.IsCompleted && pumps < 64)
        {
            uploadQueue.Pump(byteBudget: 1);
            pumps++;
        }

        Assert.That(pumps, Is.EqualTo(32));
        Assert.That(image.ContentArrival.IsCompletedSuccessfully, Is.True);
        Assert.That(image.IsContentLoaded, Is.True);
    }

    /// <summary>Tests that disposing a streamed image before Pump skips the GPU upload and completes arrival.</summary>
    [Test]
    public void CreateStreaming_ImageDisposedBeforePump_SkipsUploadAndCompletesArrival()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ImageUploadQueue uploadQueue = new(host.RenderingSystem);

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        Image image = ImageUtility.CreateStreaming(uploadQueue, new MemoryStream(data));
        Assert.That(SpinWait.SpinUntil(() => uploadQueue.PendingUploadCount == 1, TimeSpan.FromSeconds(10)), Is.True);

        image.Dispose();
        ulong uploaded = uploadQueue.Pump();

        Assert.That(uploaded, Is.Zero);
        Assert.That(image.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.IsContentLoaded, Is.False);
        Assert.That(uploadQueue.HasPendingWork, Is.False);
    }

    /// <summary>Tests that disposing the upload queue completes pending arrivals cleanly without leaking buffers.</summary>
    [Test]
    public void CreateStreaming_QueueDisposedWithPendingUploads_CompletesArrivalCleanly()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem();
        using ImageUploadQueue uploadQueue = new(host.RenderingSystem);

        byte[] data = LoadTestFile("Png", "basn6a08.png");
        using Image image = ImageUtility.CreateStreaming(uploadQueue, new MemoryStream(data));
        Assert.That(SpinWait.SpinUntil(() => uploadQueue.PendingUploadCount == 1, TimeSpan.FromSeconds(10)), Is.True);

        uploadQueue.Dispose();

        Assert.That(image.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.IsContentLoaded, Is.False);
    }

    /// <summary>Tests GPU readback after streaming an RGBA8 image in budgeted row strips across four pumps.</summary>
    [Test]
    [Category("AlcoGpu")]
    [NonParallelizable]
    public unsafe void CreateStreaming_GpuRoundTrip_BudgetedRowStripsPreserveAllPixels()
    {
        const int width = 64;
        const int height = 64;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                pixels[idx + 0] = (byte)(x * 4);
                pixels[idx + 1] = (byte)(y * 4);
                pixels[idx + 2] = (byte)((x + y) * 2);
                pixels[idx + 3] = (byte)(255 - y * 2);
            }
        }

        byte[] encodedPng = ImageEncodeUtility.EncodePng(pixels, width, height);
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.Auto);
        using ImageUploadQueue uploadQueue = new(host.RenderingSystem);
        using Image image = ImageUtility.CreateStreaming(uploadQueue, new MemoryStream(encodedPng));

        Assert.That(SpinWait.SpinUntil(() => uploadQueue.PendingUploadCount == 1, TimeSpan.FromSeconds(10)), Is.True);

        int pumpCount = 0;
        while (!image.ContentArrival.IsCompleted && pumpCount < 16)
        {
            ulong uploaded = uploadQueue.Pump(byteBudget: 4096);
            Assert.That(uploaded, Is.EqualTo(4096ul));
            pumpCount++;
        }

        Assert.That(pumpCount, Is.EqualTo(4));
        Assert.That(image.ContentArrival.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(image.IsContentLoaded, Is.True);

        using MemoryStream readbackPng = new();
        TextureUtility.EncodeTextureToPNG(host.RenderingSystem, image.Texture, readbackPng);

        byte* decoded = ImageDecodeUtility.DecodePng(readbackPng.ToArray(), out int decodedWidth, out int decodedHeight);
        try
        {
            Assert.That(decodedWidth, Is.EqualTo(width));
            Assert.That(decodedHeight, Is.EqualTo(height));
            Assert.That(new ReadOnlySpan<byte>(decoded, pixels.Length).SequenceEqual(pixels), Is.True);
        }
        finally
        {
            NativeMemory.Free(decoded);
        }
    }

    private static void PumpUntilArrived(ImageUploadQueue uploadQueue, Image image)
    {
        bool completed = SpinWait.SpinUntil(() =>
        {
            uploadQueue.Pump();
            return image.ContentArrival.IsCompleted;
        }, TimeSpan.FromSeconds(10));
        Assert.That(completed, Is.True, "Timed out waiting for image streaming upload.");
    }
}
