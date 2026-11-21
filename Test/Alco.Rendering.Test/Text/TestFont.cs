using System.Text;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>Tests immutable CPU <see cref="Font"/> metrics/SDF rasterization and <see cref="GlyphCache"/> atlas packing/upload.</summary>
[TestFixture]
public unsafe class TestFont
{
    private static byte[] LoadDefaultFontBytes()
    {
        string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Fonts", "Default.ttf");
        return File.ReadAllBytes(path);
    }

    /// <summary>Verifies pure-CPU font metrics and SDF rasterization work without any GPU device or rendering system.</summary>
    [Test]
    public void CpuMetrics_WorkWithoutGpu()
    {
        Font font = Font.Create(LoadDefaultFontBytes());

        Assert.That(font.IsEmpty, Is.False);
        Assert.That(font.Ascent, Is.GreaterThan(0f));
        Assert.That(font.Descent, Is.GreaterThanOrEqualTo(0f));
        Assert.That(font.LineHeight, Is.GreaterThanOrEqualTo(font.Ascent));
        Assert.That(font.GetAdvance(new Rune('A')), Is.GreaterThan(0f));
        Assert.That(font.GetAdvance('A'), Is.EqualTo(font.GetAdvance(new Rune('A'))));
        Assert.That(font.GetAdvance(new Rune(' ')), Is.GreaterThan(0f));
        Assert.That(font.GetAdvance('\n'), Is.EqualTo(0f));
        Assert.That(font.GetNormalizedTextWidth("Hello 世界"), Is.GreaterThan(0f));

        uint glyphId = font.GetGlyphId(new Rune('A'));
        Assert.That(glyphId, Is.Not.EqualTo(0u));
        Assert.That(font.TryRasterizeSdf(glyphId, 32, 4.0f, out GlyphRasterBitmap bitmap), Is.True);
        Assert.That(bitmap.Width, Is.GreaterThan(0));
        Assert.That(bitmap.Height, Is.GreaterThan(0));
        Assert.That(bitmap.Alpha.Length, Is.GreaterThanOrEqualTo(bitmap.Width * bitmap.Height));
    }

    /// <summary>Verifies CreateEmpty exposes valid dummy metrics with zero pages allocated even after ResolveGlyph.</summary>
    [Test]
    public void CreateEmpty_ResolvesZeroPages()
    {
        Font font = Font.CreateEmpty();
        Assert.That(font.IsEmpty, Is.True);
        Assert.That(font.Ascent, Is.GreaterThan(0f));
        Assert.That(font.LineHeight, Is.GreaterThan(0f));
        Assert.That(font.GetNormalizedTextWidth("ABC"), Is.EqualTo(1.5f).Within(1e-5f));

        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        using GlyphCache cache = new(host.RenderingSystem);

        GlyphImage glyph = cache.ResolveGlyph(font, new Rune('A'));
        Assert.That(glyph.HasVisual, Is.False);
        Assert.That(glyph.Advance, Is.EqualTo(0.5f));
        Assert.That(cache.PageCount, Is.EqualTo(0));
        Assert.That(cache.GeneratedGlyphCount, Is.EqualTo(0));
    }

    /// <summary>Verifies lazy SDF packing, gutter safety, multi-page overflow, MaxPages bound, and multi-font sharing.</summary>
    [Test]
    public void GlyphCache_PacksMultiplePagesHonorsMaxPagesAndSharesAcrossFonts()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        GlyphCacheOptions options = new(PageSize: 64, MaxPages: 3, SdfHeight: 24, SdfSpread: 3.0f, Gutter: 1);
        using GlyphCache cache = new(host.RenderingSystem, options);

        byte[] fontBytes = LoadDefaultFontBytes();
        Font fontA = Font.Create(fontBytes, "fontA");
        Font fontB = Font.Create(fontBytes, "fontB");

        GlyphImage space = cache.ResolveGlyph(fontA, new Rune(' '));
        Assert.That(space.HasVisual, Is.False);
        Assert.That(space.Advance, Is.GreaterThan(0f));
        Assert.That(cache.PageCount, Is.EqualTo(0));

        // Two distinct Font instances share the same atlas page while caching independent glyph entries.
        GlyphImage glyphA = cache.ResolveGlyph(fontA, new Rune('A'));
        GlyphImage glyphB = cache.ResolveGlyph(fontB, new Rune('A'));
        Assert.That(glyphA.HasVisual, Is.True);
        Assert.That(glyphB.HasVisual, Is.True);
        Assert.That(glyphB.Page, Is.SameAs(glyphA.Page));
        Assert.That(glyphB.UvRect, Is.Not.EqualTo(glyphA.UvRect));
        Assert.That(cache.PageCount, Is.EqualTo(1));
        Assert.That(cache.GeneratedGlyphCount, Is.EqualTo(2));

        // Printable ASCII rapidly overflows a 64x64 page at SdfHeight=24, spilling into pages 2 and 3 and hitting MaxPages=3.
        int visualCount = 1;
        for (int code = 'B'; code <= 'Z'; ++code)
        {
            GlyphImage glyph = cache.ResolveGlyph(fontA, new Rune(code));
            if (glyph.HasVisual)
            {
                ++visualCount;
                Assert.That(glyph.UvRect.X, Is.GreaterThan(0f));
                Assert.That(glyph.UvRect.Y, Is.GreaterThan(0f));
                Assert.That(glyph.UvRect.X + glyph.UvRect.Z, Is.LessThan(1f));
                Assert.That(glyph.UvRect.Y + glyph.UvRect.W, Is.LessThan(1f));
                Assert.That(glyph.Size.X, Is.GreaterThan(0f));
                Assert.That(glyph.Size.Y, Is.GreaterThan(0f));
                Assert.That(glyph.IsDistanceField, Is.True);
            }
        }

        Assert.That(visualCount, Is.GreaterThan(4));
        Assert.That(cache.PageCount, Is.EqualTo(options.MaxPages));
        Assert.That(cache.AtlasBytes, Is.EqualTo((long)cache.PageCount * 64 * 64));
    }

    /// <summary>Verifies system-queued glyph uploads survive discarded frames and commit same-shelf siblings cleanly.</summary>
    [TestCase(GraphicsBackend.NoGPU)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void GlyphCache_UploadSurvivesDiscardFrameAndCommitsSameShelfSiblings(GraphicsBackend backend)
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(backend);
        GlyphCacheOptions options = new(PageSize: 256, MaxPages: 4, SdfHeight: 32, SdfSpread: 4.0f, Gutter: 1);
        using GlyphCache cache = new(host.RenderingSystem, options);
        Font font = Font.Create(LoadDefaultFontBytes());
        RenderContext context = new(host.RenderingSystem);
        try
        {
            // Frame 1: resolve 'A' (queued via RenderingSystem.UploadTexture), then DISCARD frame -> must survive.
            GlyphImage glyphA = cache.ResolveGlyph(font, new Rune('A'));
            Assert.That(glyphA.HasVisual, Is.True);

            context.BeginFrame();
            context.DiscardFrame();

            // Frame 2: submit an empty frame -> system flushes queued uploads before frame submission.
            context.BeginFrame();
            context.Submit().Wait();

            // Frame 3: resolve 'B' (shares shelf 0 with 'A') -> disjoint cell upload commits cleanly alongside 'A'.
            GlyphImage glyphB = cache.ResolveGlyph(font, new Rune('B'));
            Assert.That(glyphB.HasVisual, Is.True);
            Assert.That(glyphB.Page, Is.SameAs(glyphA.Page));

            context.BeginFrame();
            context.Submit().Wait();

            if (backend == GraphicsBackend.NoGPU)
                return;

            // Read back the R8Unorm page and verify both 'A' and 'B' SDF regions contain coverage while gutter stays zero.
            GPUHeap readback = host.Device.AllocateGPUHeap(256UL * 256UL, MemoryType.Readback);
            try
            {
                context.BeginFrame();
                context.CopyTextureToMemory(glyphA.Page!.NativeTexture, readback.Range.GPURange.Slice(0, 256UL * 256UL));
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
                context.Submit().Wait();

                byte* texels = readback.Range.Cpu;
                byte maxA = SampleMaxInRect(texels, 256, glyphA.UvRect);
                byte maxB = SampleMaxInRect(texels, 256, glyphB.UvRect);
                Assert.That(maxA, Is.GreaterThan((byte)160), "Glyph A SDF core should have high coverage.");
                Assert.That(maxB, Is.GreaterThan((byte)160), "Same-shelf sibling Glyph B SDF core should also upload cleanly.");
                Assert.That(texels[0], Is.EqualTo((byte)0), "Top-left gutter texel must remain zero.");
            }
            finally
            {
                readback.Destroy();
            }
        }
        finally
        {
            context.Destroy();
        }
    }

    private static byte SampleMaxInRect(byte* texels, int pageSize, System.Numerics.Vector4 uvRect)
    {
        int x0 = (int)MathF.Floor(uvRect.X * pageSize);
        int y0 = (int)MathF.Floor(uvRect.Y * pageSize);
        int x1 = Math.Min(pageSize, (int)MathF.Ceiling((uvRect.X + uvRect.Z) * pageSize));
        int y1 = Math.Min(pageSize, (int)MathF.Ceiling((uvRect.Y + uvRect.W) * pageSize));
        byte peak = 0;
        for (int y = y0; y < y1; ++y)
        {
            for (int x = x0; x < x1; ++x)
            {
                byte value = texels[y * pageSize + x];
                if (value > peak)
                    peak = value;
            }
        }
        return peak;
    }
}
