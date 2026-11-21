using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Multi-font lazy SDF glyph atlas cache backed by <see cref="RenderingSystem"/> textures.</summary>
public sealed class GlyphCache : AutoDisposable
{
    private const int AsciiTableSize = 128;

    private sealed class AtlasPage(Texture texture)
    {
        public readonly Texture Texture = texture;
        public int CursorX;
        public int CursorY;
        public int ShelfHeight;
    }

    private sealed class FontEntry
    {
        public readonly GlyphImage[] AsciiGlyphs = new GlyphImage[AsciiTableSize];
        public readonly Dictionary<uint, GlyphImage> Glyphs = new();
        private ulong _asciiMaskLo;
        private ulong _asciiMaskHi;

        public bool TryGetAscii(uint codePoint, out GlyphImage image)
        {
            ulong bit = 1ul << (int)(codePoint & 63);
            ulong mask = codePoint < 64 ? _asciiMaskLo : _asciiMaskHi;
            if ((mask & bit) == 0)
            {
                image = default;
                return false;
            }

            image = AsciiGlyphs[codePoint];
            return true;
        }

        public void SetAscii(uint codePoint, in GlyphImage image)
        {
            AsciiGlyphs[codePoint] = image;
            ulong bit = 1ul << (int)(codePoint & 63);
            if (codePoint < 64)
                _asciiMaskLo |= bit;
            else
                _asciiMaskHi |= bit;
        }
    }

    private readonly RenderingSystem _system;
    private readonly GlyphCacheOptions _options;
    private readonly ConditionalWeakTable<Font, FontEntry> _entries = new();
    private readonly List<AtlasPage> _pages = new(4);
    private byte[] _uploadScratch = [];
    private Font? _lastFont;
    private FontEntry? _lastEntry;

    /// <summary>Configuration used when rasterizing and packing SDF glyphs into atlas pages.</summary>
    public GlyphCacheOptions Options => _options;

    /// <summary>Total number of GPU atlas pages currently allocated by this cache.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Total number of glyphs whose bitmaps have been rasterized and queued into atlas pages.</summary>
    public int GeneratedGlyphCount { get; private set; }

    /// <summary>Total GPU atlas memory occupied by all allocated pages in bytes.</summary>
    public long AtlasBytes => (long)_pages.Count * _options.PageSize * _options.PageSize;

    /// <summary>Creates a lazy multi-font SDF glyph cache backed by <paramref name="renderingSystem"/>.</summary>
    /// <param name="renderingSystem">The rendering system used to allocate and upload atlas pages.</param>
    /// <param name="options">Optional SDF height, spread, gutter, and page sizing parameters.</param>
    public GlyphCache(RenderingSystem renderingSystem, GlyphCacheOptions? options = null)
    {
        _system = renderingSystem ?? throw new ArgumentNullException(nameof(renderingSystem));
        GlyphCacheOptions resolved = options ?? GlyphCacheOptions.Default;
        ValidateOptions(in resolved);
        _options = resolved;
    }

    /// <summary>Resolves <paramref name="rune"/> for <paramref name="font"/>, lazily rasterizing and uploading its SDF bitmap if needed.</summary>
    /// <param name="font">The static font asset to resolve from.</param>
    /// <param name="rune">The Unicode scalar value to resolve.</param>
    /// <returns>Layout metrics and atlas page coordinates for drawing.</returns>
    public GlyphImage ResolveGlyph(Font font, Rune rune)
    {
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (rune.Value is '\r' or '\n')
            return default;

        if (font.IsEmpty)
            return new GlyphImage { Advance = font.GetAdvance(rune), IsDistanceField = true };

        FontEntry entry = GetOrCreateEntry(font);
        uint codePoint = (uint)rune.Value;
        bool isAscii = codePoint < AsciiTableSize;
        if (isAscii && entry.TryGetAscii(codePoint, out GlyphImage cached))
            return cached;

        uint glyphId = font.GetGlyphId(rune);
        if (entry.Glyphs.TryGetValue(glyphId, out cached))
        {
            if (isAscii)
                entry.SetAscii(codePoint, in cached);
            return cached;
        }

        float advance = font.GetAdvanceForGlyph(glyphId);
        if (!font.TryRasterizeSdf(glyphId, _options.SdfHeight, _options.SdfSpread, out GlyphRasterBitmap bitmap))
        {
            GlyphImage empty = new() { Advance = advance, IsDistanceField = true };
            CacheGlyph(entry, glyphId, codePoint, isAscii, in empty);
            return empty;
        }

        if (!TryPackAndUploadGlyph(bitmap.Width, bitmap.Height, bitmap.Alpha, out Texture? pageTexture, out Vector4 uvRect))
        {
            GlyphImage overflow = new() { Advance = advance, IsDistanceField = true };
            CacheGlyph(entry, glyphId, codePoint, isAscii, in overflow);
            return overflow;
        }

        float invSdfHeight = 1.0f / _options.SdfHeight;
        GlyphImage image = new()
        {
            Page = pageTexture,
            UvRect = uvRect,
            Offset = new Vector2(bitmap.Left * invSdfHeight, bitmap.Top * invSdfHeight),
            Size = new Vector2(bitmap.Width * invSdfHeight, bitmap.Height * invSdfHeight),
            Advance = advance,
            IsDistanceField = true,
        };
        ++GeneratedGlyphCount;
        CacheGlyph(entry, glyphId, codePoint, isAscii, in image);
        return image;
    }

    /// <summary>Resolves a UTF-16 BMP character for <paramref name="font"/>, lazily rasterizing and uploading its SDF bitmap if needed.</summary>
    /// <param name="font">The static font asset to resolve from.</param>
    /// <param name="c">The character to resolve.</param>
    /// <returns>Layout metrics and atlas page coordinates for drawing.</returns>
    public GlyphImage ResolveGlyph(Font font, char c)
    {
        return ResolveGlyph(font, Rune.TryCreate(c, out Rune rune) ? rune : Rune.ReplacementChar);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        for (int index = 0; index < _pages.Count; ++index)
        {
            Texture pageTexture = _pages[index].Texture;
            if (!pageTexture.IsDestroyed)
                pageTexture.Destroy();
        }
        _pages.Clear();
        _entries.Clear();
        _lastFont = null;
        _lastEntry = null;
        _uploadScratch = [];
    }

    private static void ValidateOptions(in GlyphCacheOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PageSize, 16, nameof(options.PageSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxPages, 1, nameof(options.MaxPages));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.SdfHeight, 4, nameof(options.SdfHeight));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.SdfSpread, 0f, nameof(options.SdfSpread));
        ArgumentOutOfRangeException.ThrowIfNegative(options.Gutter, nameof(options.Gutter));
    }

    private FontEntry GetOrCreateEntry(Font font)
    {
        if (ReferenceEquals(font, _lastFont) && _lastEntry != null)
            return _lastEntry;

        if (!_entries.TryGetValue(font, out FontEntry? entry))
        {
            entry = new FontEntry();
            _entries.Add(font, entry);
        }

        _lastFont = font;
        _lastEntry = entry;
        return entry;
    }

    private static void CacheGlyph(FontEntry entry, uint glyphId, uint codePoint, bool isAscii, in GlyphImage image)
    {
        entry.Glyphs.Add(glyphId, image);
        if (isAscii)
            entry.SetAscii(codePoint, in image);
    }

    private bool TryPackAndUploadGlyph(int width, int height, byte[] alpha, out Texture? pageTexture, out Vector4 uvRect)
    {
        int gutter = _options.Gutter;
        int pageSize = _options.PageSize;
        int cellWidth = width + gutter * 2;
        int cellHeight = height + gutter * 2;

        if (cellWidth > pageSize || cellHeight > pageSize)
        {
            pageTexture = null;
            uvRect = default;
            return false;
        }

        AtlasPage? targetPage = null;
        if (_pages.Count > 0)
        {
            AtlasPage last = _pages[^1];
            if (CanPlaceOnPage(last, cellWidth, cellHeight, pageSize))
                targetPage = last;
        }

        if (targetPage == null)
        {
            if (_pages.Count >= _options.MaxPages)
            {
                pageTexture = null;
                uvRect = default;
                return false;
            }
            targetPage = AllocatePage();
        }

        PlaceAndUploadOnPage(targetPage, width, height, alpha, gutter, pageSize, out uvRect);
        pageTexture = targetPage.Texture;
        return true;
    }

    private static bool CanPlaceOnPage(AtlasPage page, int cellWidth, int cellHeight, int pageSize)
    {
        int x = page.CursorX;
        int y = page.CursorY;
        int shelfHeight = page.ShelfHeight;

        if (x + cellWidth > pageSize)
        {
            x = 0;
            y += shelfHeight;
        }

        return x + cellWidth <= pageSize && y + cellHeight <= pageSize;
    }

    private AtlasPage AllocatePage()
    {
        uint size = (uint)_options.PageSize;
        Texture texture = _system.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(size, size, 1),
            Format = Format.R8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        AtlasPage page = new(texture);
        _pages.Add(page);
        return page;
    }

    private void PlaceAndUploadOnPage(
        AtlasPage page,
        int width,
        int height,
        byte[] alpha,
        int gutter,
        int pageSize,
        out Vector4 uvRect)
    {
        int cellWidth = width + gutter * 2;
        int cellHeight = height + gutter * 2;

        if (page.CursorX + cellWidth > pageSize)
        {
            page.CursorX = 0;
            page.CursorY += page.ShelfHeight;
            page.ShelfHeight = 0;
        }

        int cellX = page.CursorX;
        int cellY = page.CursorY;
        page.CursorX += cellWidth;
        page.ShelfHeight = Math.Max(page.ShelfHeight, cellHeight);

        ReadOnlySpan<byte> uploadBytes;
        if (gutter == 0)
        {
            uploadBytes = alpha.AsSpan(0, width * height);
        }
        else
        {
            int cellByteCount = cellWidth * cellHeight;
            if (_uploadScratch.Length < cellByteCount)
            {
                int nextCapacity = Math.Max(cellByteCount, Math.Max(256, _uploadScratch.Length * 2));
                _uploadScratch = new byte[nextCapacity];
            }

            Span<byte> scratch = _uploadScratch.AsSpan(0, cellByteCount);
            scratch.Clear();
            for (int row = 0; row < height; ++row)
            {
                alpha.AsSpan(row * width, width).CopyTo(scratch.Slice((gutter + row) * cellWidth + gutter, width));
            }
            uploadBytes = scratch;
        }

        _system.UploadTexture(page.Texture, uploadBytes, new TextureCopyDesc
        {
            Offset = new Extent3D((uint)cellX, (uint)cellY, 0),
            Extent = new Extent3D((uint)cellWidth, (uint)cellHeight, 1),
            RowPitchBytes = (ulong)cellWidth,
        });

        float invPage = 1.0f / pageSize;
        uvRect = new Vector4((cellX + gutter) * invPage, (cellY + gutter) * invPage, width * invPage, height * invPage);
    }
}
