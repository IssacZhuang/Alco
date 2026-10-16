using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.AlcoGpu;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>
/// Real-GPU rasterization tests: coverage, fill rules, stroke caps, gradients and
/// the weak texture cache, verified numerically via texture readback. Encoding the
/// results as PNGs next to the test output enables manual visual inspection.
/// Requires a graphics adapter; skips otherwise.
/// </summary>
[TestFixture]
[Category("AlcoGpu")]
[NonParallelizable]
public unsafe class TestSvgRendererGpu
{
    /// <summary>Creates a rendering system over a real device serving the engine shader assets.</summary>
    private static DummyRenderingSystemHost CreateHost()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Alco.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.That(directory, Is.Not.Null, "Alco.slnx must be reachable from the test binary");
        string shaderRoot = Path.Combine(directory!.FullName, "Src", "Alco.Rendering", "Assets", "Shaders");
        string[] shaderFiles = Directory.GetFiles(shaderRoot, "*.slang", SearchOption.AllDirectories);
        var resolver = ShaderModuleResolver.Create(
            path =>
            {
                string candidate = Path.Combine(shaderRoot, SlangPathUtility.NormalizePath(path));
                return File.Exists(candidate) ? File.OpenRead(candidate) : null;
            },
            () => shaderFiles.Select(path => Path.GetRelativePath(shaderRoot, path).Replace('\\', '/')));
        GpuHost gpuHost = new();
        AlcoGpuDevice? device = null;
        try
        {
            device = new AlcoGpuDevice(new DeviceDescriptor(gpuHost, GraphicsBackend.Auto));
        }
        catch (Exception)
        {
            gpuHost.Dispose();
            Assert.Ignore("No usable graphics adapter is available.");
        }
        return Utility.CreateRenderingSystem(resolver, device!);
    }

    /// <summary>Reads the whole texture back as premultiplied RGBA8 bytes.</summary>
    private static byte[] ReadPixels(Alco.Rendering.RenderingSystem renderingSystem, Texture2D texture)
    {
        byte[] pixels = new byte[texture.Width * texture.Height * 4];
        fixed (byte* pointer = pixels)
        {
            renderingSystem.GraphicsDevice.ReadTexture(texture.NativeTexture, pointer, (uint)pixels.Length);
        }
        return pixels;
    }

    private static (byte R, byte G, byte B, byte A) Pixel(byte[] pixels, uint width, int x, int y)
    {
        int index = (y * (int)width + x) * 4;
        return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
    }

    private static string Svg(uint width, uint height, string body)
    {
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\">{body}</svg>";
    }

    /// <summary>Opaque rectangles produce hard edges on integer-aligned geometry:
    /// covered pixels full, neighbors empty.</summary>
    [Test]
    public void SolidRect_CoversInteriorAndLeavesExteriorEmpty()
    {
        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        using Texture2D texture = renderer.GetTexture(Svg(8, 8,
            "<rect x=\"2\" y=\"2\" width=\"4\" height=\"4\" fill=\"#ff0000\"/>"));
        Assert.That(texture.Width, Is.EqualTo(8u));
        Assert.That(texture.Height, Is.EqualTo(8u));
        byte[] pixels = ReadPixels(host.RenderingSystem, texture);

        foreach ((int x, int y) in new[] { (4, 4), (2, 2), (5, 5), (3, 3) })
        {
            Assert.That(Pixel(pixels, 8, x, y).A, Is.EqualTo(255), $"interior ({x},{y})");
            Assert.That(Pixel(pixels, 8, x, y).R, Is.EqualTo(255), $"interior ({x},{y}) red");
        }
        foreach ((int x, int y) in new[] { (0, 0), (1, 4), (6, 4), (7, 7), (4, 1), (4, 6) })
        {
            Assert.That(Pixel(pixels, 8, x, y).A, Is.EqualTo(0), $"exterior ({x},{y})");
        }
    }

    /// <summary>Even-odd fill punches a hole: the inner rect area stays empty.</summary>
    [Test]
    public void EvenOddDonut_HoleStaysEmpty()
    {
        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        using Texture2D texture = renderer.GetTexture(Svg(8, 8,
            "<path d=\"M1 1h6v6h-6z M3 3h2v2h-2z\" fill-rule=\"evenodd\" fill=\"#00ff00\"/>"));
        byte[] pixels = ReadPixels(host.RenderingSystem, texture);

        Assert.That(Pixel(pixels, 8, 4, 4).A, Is.EqualTo(0), "hole center is empty");
        Assert.That(Pixel(pixels, 8, 3, 4).A, Is.EqualTo(0), "hole edge pixel (3,4) is inside the hole");
        Assert.That(Pixel(pixels, 8, 2, 4).A, Is.EqualTo(255), "ring pixel is covered");
        Assert.That(Pixel(pixels, 8, 2, 4).G, Is.EqualTo(255), "ring is green");
        Assert.That(Pixel(pixels, 8, 6, 6).A, Is.EqualTo(255), "outer edge is covered");
    }

    /// <summary>Round caps extend the stroke past its endpoints by half the width.</summary>
    [Test]
    public void RoundCap_ExtendsBeyondEndpoints()
    {
        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        // Spine (2,4)-(6,4), radius 2: the caps fully cover their endpoint pixels.
        using Texture2D texture = renderer.GetTexture(Svg(8, 8,
            "<line x1=\"2\" y1=\"4\" x2=\"6\" y2=\"4\" fill=\"none\" " +
            "stroke=\"#0000ff\" stroke-width=\"4\" stroke-linecap=\"round\"/>"));
        byte[] pixels = ReadPixels(host.RenderingSystem, texture);

        foreach ((int x, int y) in new[] { (4, 4), (1, 4), (6, 4) })
        {
            Assert.That(Pixel(pixels, 8, x, y).A, Is.EqualTo(255), $"inside cap ({x},{y})");
            Assert.That(Pixel(pixels, 8, x, y).B, Is.EqualTo(255), $"inside cap ({x},{y}) blue");
        }
        Assert.That(Pixel(pixels, 8, 4, 1).A, Is.EqualTo(0), "above the spine");
        Assert.That(Pixel(pixels, 8, 4, 7).A, Is.EqualTo(0), "below the spine");
        Assert.That(Pixel(pixels, 8, 0, 0).A, Is.EqualTo(0), "before the start cap");
        Assert.That(Pixel(pixels, 8, 7, 7).A, Is.EqualTo(0), "past the end cap");
    }

    /// <summary>A vertical linear gradient interpolates red to blue along y.</summary>
    [Test]
    public void LinearGradient_InterpolatesAlongAxis()
    {
        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        using Texture2D texture = renderer.GetTexture(Svg(2, 8, """
            <defs>
                <linearGradient id="g" x1="0%" y1="0%" x2="0%" y2="100%">
                    <stop offset="0" stop-color="#ff0000"/>
                    <stop offset="1" stop-color="#0000ff"/>
                </linearGradient>
            </defs>
            <rect x="0" y="0" width="2" height="8" fill="url(#g)"/>
            """));
        byte[] pixels = ReadPixels(host.RenderingSystem, texture);

        var top = Pixel(pixels, 2, 0, 0);
        var bottom = Pixel(pixels, 2, 0, 7);
        var middle = Pixel(pixels, 2, 0, 4);
        Assert.That(top.R, Is.GreaterThan(220), "top is red-dominant");
        Assert.That(top.B, Is.LessThan(40));
        Assert.That(bottom.R, Is.LessThan(40), "bottom is blue-dominant");
        Assert.That(bottom.B, Is.GreaterThan(220));
        Assert.That(middle.R, Is.InRange(80, 180), "middle blends both stops");
        Assert.That(middle.B, Is.InRange(80, 180));
        Assert.That(middle.A, Is.EqualTo(255), "gradient stays opaque");
    }

    /// <summary>Overlapping shapes composite in document order: the later red
    /// rect blends over the earlier green one with alpha.</summary>
    [Test]
    public void TranslucentOverlap_CompositesInDocumentOrder()
    {
        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        using Texture2D texture = renderer.GetTexture(Svg(4, 4,
            "<rect x=\"0\" y=\"0\" width=\"4\" height=\"4\" fill=\"#00ff00\"/>" +
            "<rect x=\"0\" y=\"0\" width=\"2\" height=\"4\" fill=\"#ff0000\" fill-opacity=\"0.5\"/>"));
        byte[] pixels = ReadPixels(host.RenderingSystem, texture);

        // Left half: red at 50% over green.
        var left = Pixel(pixels, 4, 0, 0);
        Assert.That(left.A, Is.EqualTo(255));
        Assert.That(left.R, Is.EqualTo(128).Within(2), "half-opaque red over opaque green");
        Assert.That(left.G, Is.EqualTo(128).Within(2));
        // Right half: pure green.
        var right = Pixel(pixels, 4, 3, 0);
        Assert.That(right.G, Is.EqualTo(255));
        Assert.That(right.R, Is.EqualTo(0));
    }

    /// <summary>The weak cache returns the live texture instance, re-rasterizes
    /// after the caller disposes it, and answers cache hits without allocating.</summary>
    [Test]
    public void Cache_ReusesLiveTextureAndRebuildsAfterDispose()
    {
        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        string svg = Svg(8, 8, "<rect x=\"2\" y=\"2\" width=\"4\" height=\"4\" fill=\"#ffffff\"/>");

        Texture2D first = renderer.GetTexture(svg);
        Texture2D second = renderer.GetTexture(svg);
        Assert.That(second, Is.SameAs(first), "a live texture is reused");

        // Hot-path contract: hits resolve without allocating (no re-parse, no byte[]
        // for hashing). Raw asserts inside the loop would allocate, so the loop only
        // accumulates references and everything is checked after the measurement.
        Texture2D hit = first;
        Texture2D explicitHit = first;
        for (int i = 0; i < 10; i++)
        {
            hit = renderer.GetTexture(svg);
            explicitHit = renderer.GetTexture(svg, 8, 8);
        }
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            hit = renderer.GetTexture(svg);
        }
        long intrinsicDelta = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            explicitHit = renderer.GetTexture(svg, 8, 8);
        }
        long explicitDelta = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        TestContext.Progress.WriteLine($"intrinsic loop: {intrinsicDelta} bytes, explicit loop: {explicitDelta} bytes");
        Assert.That(ReferenceEquals(hit, first), Is.True, "hot-path calls return the cached instance");
        Assert.That(ReferenceEquals(explicitHit, first), Is.True,
            "explicit dimensions matching the intrinsic size share the cache entry");
        Assert.That(intrinsicDelta + explicitDelta, Is.EqualTo(0),
            "cache hits allocate nothing");

        first.Dispose();
        Texture2D third = renderer.GetTexture(svg);
        Assert.That(third, Is.Not.SameAs(first), "a disposed texture re-rasterizes");
        Assert.That(third.IsDisposed, Is.False);
        byte[] pixels = ReadPixels(host.RenderingSystem, third);
        Assert.That(Pixel(pixels, 8, 4, 4).A, Is.EqualTo(255), "rebuilt texture has content");
        third.Dispose();
    }

    /// <summary>Explicit sizes rasterize the same document at different resolutions.</summary>
    [Test]
    public void ExplicitSize_RasterizesAtRequestedSize()
    {
        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        string svg = Svg(8, 8, "<rect x=\"2\" y=\"2\" width=\"4\" height=\"4\" fill=\"#ffffff\"/>");
        using Texture2D small = renderer.GetTexture(svg, 16, 16);
        using Texture2D large = renderer.GetTexture(svg, 64, 32);
        Assert.That(small.Width, Is.EqualTo(16u));
        Assert.That(small.Height, Is.EqualTo(16u));
        Assert.That(large.Width, Is.EqualTo(64u));
        Assert.That(large.Height, Is.EqualTo(32u));

        byte[] pixels = ReadPixels(host.RenderingSystem, large);
        // Scale 16x16-fit → content occupies the middle half; center row solid.
        Assert.That(Pixel(pixels, 64, 32, 16).A, Is.EqualTo(255), "scaled interior is covered");
        Assert.That(Pixel(pixels, 64, 0, 16).A, Is.EqualTo(0), "letterbox column is empty");
    }

    /// <summary>Invalid documents fail cleanly through both entry points.</summary>
    [Test]
    public void InvalidDocument_ReturnsFalseOrThrows()
    {
        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        Assert.That(renderer.TryGetTexture("not an svg document", out Texture2D? texture), Is.False);
        Assert.That(texture, Is.Null);
        Assert.Throws<ArgumentException>(() => renderer.GetTexture("<broken"));
    }

    /// <summary>Real artwork from the game repository rasterizes; outputs a PNG
    /// per document for manual visual inspection.</summary>
    [Test]
    public void ArtworkFiles_RasterizeAndEncode()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Alco.slnx")))
        {
            directory = directory.Parent;
        }
        string? artworkRoot = directory?.Parent is { } parent
            ? Path.Combine(parent.FullName, "Artwork", "Textures", "UI")
            : null;
        if (artworkRoot == null || !Directory.Exists(artworkRoot))
        {
            Assert.Ignore("The Artwork directory is not available next to the Alco repository.");
        }
        string[] files = Directory.GetFiles(artworkRoot, "*.svg", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty, "artwork SVGs must exist");

        using DummyRenderingSystemHost host = CreateHost();
        using SvgRenderer renderer = host.RenderingSystem.CreateSvgRenderer();
        string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "SvgArtwork");
        Directory.CreateDirectory(outputDirectory);
        foreach (string file in files)
        {
            Texture2D texture = renderer.GetTexture(File.ReadAllText(file));
            Assert.That(texture.Width, Is.GreaterThan(0u), $"{Path.GetFileName(file)} width");
            Assert.That(texture.Height, Is.GreaterThan(0u), $"{Path.GetFileName(file)} height");
            using FileStream stream = File.Create(
                Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(file) + ".png"));
            host.RenderingSystem.EncodeTextureToPNG(texture, stream);
            texture.Dispose();
        }
        TestContext.Progress.WriteLine($"Encoded {files.Length} artwork SVGs to {outputDirectory}");
    }

    private sealed class GpuHost : IGPUDeviceHost, IDisposable
    {
        /// <inheritdoc />
        public event Action OnEndFrame { add { } remove { } }
        /// <inheritdoc />
        public event Action? OnDispose;
        /// <inheritdoc />
        public void Dispose() => OnDispose?.Invoke();
        /// <inheritdoc />
        public void LogInfo(ReadOnlySpan<char> message) { }
        /// <inheritdoc />
        public void LogWarning(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogError(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }
}
