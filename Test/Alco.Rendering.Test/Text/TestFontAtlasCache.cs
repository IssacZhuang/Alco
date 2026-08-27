using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco;
using Alco.IO;
using NUnit.Framework;

namespace Alco.Rendering.Test;

[TestFixture]
public class TestFontAtlasCache
{
    private const int TestAtlasWidth = 1024;
    private const int TestAtlasHeight = 1024;
    private const int TestFontSize = 32;

    private static readonly int2[] TestRanges =
    [
        UnicodeUtility.RangeBasicLatin,
        UnicodeUtility.RangeKatakana,
    ];

    private static byte[] LoadDefaultTtf()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "Files", "Fonts", "Default.ttf");
        Assert.That(File.Exists(path), Is.True, $"Test font not found at {path}");
        return File.ReadAllBytes(path);
    }

    private static string CreateTempCacheDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AlcoFontAtlasCacheTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void BakeAtlas(byte[] ttf, out byte[] bitmap, out GlyphInfo[] glyphs)
    {
        using FontAtlasPacker packer = new FontAtlasPacker(TestAtlasWidth, TestAtlasHeight, 1);
        packer.Add(ttf, TestFontSize, TestRanges);
        bitmap = packer.Bitmap.ToArray();
        glyphs = packer.Glyphs;
    }

    [Test]
    public void StoreThenLoad_RoundTripsAtlasExactly()
    {
        string directory = CreateTempCacheDirectory();
        try
        {
            FontAtlasCache cache = new FontAtlasCache(directory);
            byte[] ttf = LoadDefaultTtf();
            BakeAtlas(ttf, out byte[] bitmap, out GlyphInfo[] glyphs);

            Assert.That(cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges), Is.Null);

            cache.StoreAsync(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges, bitmap, glyphs).Wait();

            FontAtlasCacheEntry? entry = cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges);
            Assert.That(entry, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(entry!.Width, Is.EqualTo(TestAtlasWidth));
                Assert.That(entry.Height, Is.EqualTo(TestAtlasHeight));
                Assert.That(entry.Glyphs.Length, Is.EqualTo(glyphs.Length));
                Assert.That(entry.Bitmap.ToArray(), Is.EqualTo(bitmap));
                Assert.That(MemoryMarshal.AsBytes<GlyphInfo>(entry.Glyphs).ToArray(), Is.EqualTo(MemoryMarshal.AsBytes<GlyphInfo>(glyphs).ToArray()));
            });

            // The stored entry must actually be compressed, not a raw copy.
            string[] files = Directory.GetFiles(directory, "*.bin");
            Assert.That(files.Length, Is.EqualTo(1));
            long rawSize = bitmap.Length + (long)glyphs.Length * Unsafe.SizeOf<GlyphInfo>();
            Assert.That(new FileInfo(files[0]).Length, Is.LessThan(rawSize / 2));
            TestContext.Out.WriteLine($"raw {rawSize / 1024} KiB -> compressed {new FileInfo(files[0]).Length / 1024} KiB");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void TryLoad_ReturnsNull_ForDifferentBakeInputs()
    {
        string directory = CreateTempCacheDirectory();
        try
        {
            FontAtlasCache cache = new FontAtlasCache(directory);
            byte[] ttf = LoadDefaultTtf();
            BakeAtlas(ttf, out byte[] bitmap, out GlyphInfo[] glyphs);
            cache.StoreAsync(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges, bitmap, glyphs).Wait();

            // Different font size, padding, atlas size, SDF flag and ranges must all miss.
            Assert.That(cache.TryLoad(ttf, TestFontSize + 1, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges), Is.Null);
            Assert.That(cache.TryLoad(ttf, TestFontSize, 6, TestAtlasWidth, TestAtlasHeight, false, TestRanges), Is.Null);
            Assert.That(cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth + 1, TestAtlasHeight, false, TestRanges), Is.Null);
            Assert.That(cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, true, TestRanges), Is.Null);
            Assert.That(cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, [UnicodeUtility.RangeBasicLatin]), Is.Null);

            // Only the original key hits.
            Assert.That(cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges), Is.Not.Null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase(0)]
    [TestCase(64)]
    [TestCase(1024)]
    public void TryLoad_ReturnsNull_ForCorruptEntries(int corruptSize)
    {
        string directory = CreateTempCacheDirectory();
        try
        {
            FontAtlasCache cache = new FontAtlasCache(directory);
            byte[] ttf = LoadDefaultTtf();
            BakeAtlas(ttf, out byte[] bitmap, out GlyphInfo[] glyphs);
            cache.StoreAsync(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges, bitmap, glyphs).Wait();

            string entry = Directory.GetFiles(directory, "*.bin")[0];
            File.WriteAllBytes(entry, new byte[corruptSize]);

            Assert.That(cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges), Is.Null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void StoreAsync_SwallowsDuplicateConcurrentWrites()
    {
        string directory = CreateTempCacheDirectory();
        try
        {
            FontAtlasCache cache = new FontAtlasCache(directory);
            byte[] ttf = LoadDefaultTtf();
            BakeAtlas(ttf, out byte[] bitmap, out GlyphInfo[] glyphs);

            Task first = cache.StoreAsync(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges, bitmap, glyphs);
            Task duplicate = cache.StoreAsync(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges, bitmap, glyphs);
            first.Wait();
            duplicate.Wait();

            string[] files = Directory.GetFiles(directory, "*.bin");
            Assert.That(files.Length, Is.EqualTo(1));
            Assert.That(cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges), Is.Not.Null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DisabledCache_NeverLoadsOrStores()
    {
        FontAtlasCache cache = new FontAtlasCache(null);
        byte[] ttf = LoadDefaultTtf();

        Assert.That(cache.IsEnabled, Is.False);
        Assert.That(cache.TryLoad(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges), Is.Null);

        BakeAtlas(ttf, out byte[] bitmap, out GlyphInfo[] glyphs);
        Assert.That(cache.StoreAsync(ttf, TestFontSize, 1, TestAtlasWidth, TestAtlasHeight, false, TestRanges, bitmap, glyphs).IsCompleted, Is.True);
    }

    /// <summary>
    /// End-to-end check through the asset loader with the full default unicode range
    /// set (the startup scenario): the first load rasterizes ~40k glyphs, the second
    /// load must hit the persistent cache and produce an identical font, faster.
    /// </summary>
    [Test]
    public void AssetLoaderFontTTF_SecondLoad_HitsCacheAndMatchesGlyphs()
    {
        using DummyRenderingSystemHost host = Utility.CreateRenderingSystem();
        string directory = CreateTempCacheDirectory();
        try
        {
            AssetLoaderFontTTF loader = new AssetLoaderFontTTF(host.RenderingSystem, cacheDirectory: directory);
            byte[] ttf = LoadDefaultTtf();

            Stopwatch firstStopwatch = Stopwatch.StartNew();
            using Font first = (Font)loader.CreateAsset(new AssetLoadContext(null!, "Fonts/Default.ttf", ttf, typeof(Font)));
            firstStopwatch.Stop();

            // The cache write is asynchronous and the loader discards its task;
            // wait until the entry has been durably moved into place.
            string[] files = [];
            for (int i = 0; i < 300 && files.Length == 0; i++)
            {
                Thread.Sleep(100);
                files = Directory.GetFiles(directory, "*.bin");
            }
            Assert.That(files.Length, Is.EqualTo(1), "Font atlas cache entry was never written");

            Stopwatch secondStopwatch = Stopwatch.StartNew();
            using Font second = (Font)loader.CreateAsset(new AssetLoadContext(null!, "Fonts/Default.ttf", ttf, typeof(Font)));
            secondStopwatch.Stop();

            char[] samples = ['A', 'あ', '中', '한', '，'];
            foreach (char c in samples)
            {
                GlyphInfo a = first.GetGlyph(c);
                GlyphInfo b = second.GetGlyph(c);
                Assert.Multiple(() =>
                {
                    Assert.That(b.UVRect, Is.EqualTo(a.UVRect), $"UVRect mismatch for U+{(int)c:X4}");
                    Assert.That(b.Size, Is.EqualTo(a.Size), $"Size mismatch for U+{(int)c:X4}");
                    Assert.That(b.Offset, Is.EqualTo(a.Offset), $"Offset mismatch for U+{(int)c:X4}");
                    Assert.That(b.Advance, Is.EqualTo(a.Advance), $"Advance mismatch for U+{(int)c:X4}");
                });
            }

            Assert.That(secondStopwatch.Elapsed, Is.LessThan(firstStopwatch.Elapsed),
                $"cached load ({secondStopwatch.ElapsedMilliseconds} ms) should be faster than rasterized load ({firstStopwatch.ElapsedMilliseconds} ms)");
            TestContext.Out.WriteLine($"first load: {firstStopwatch.ElapsedMilliseconds} ms, cached load: {secondStopwatch.ElapsedMilliseconds} ms");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
