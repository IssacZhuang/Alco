using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco;

namespace Alco.Rendering;

/// <summary>
/// A baked font atlas loaded from <see cref="FontAtlasCache"/>.
/// </summary>
public sealed class FontAtlasCacheEntry
{
    private readonly byte[] _bitmap;
    private readonly GlyphInfo[] _glyphs;
    private readonly int _width;
    private readonly int _height;

    /// <summary>
    /// The R8 coverage bitmap of the atlas, <c>Width * Height</c> bytes.
    /// </summary>
    public ReadOnlySpan<byte> Bitmap => _bitmap;

    /// <summary>
    /// The raw packer glyph table, before any SDF padding adjustment.
    /// </summary>
    public GlyphInfo[] Glyphs => _glyphs;

    public int Width => _width;

    public int Height => _height;

    public FontAtlasCacheEntry(byte[] bitmap, GlyphInfo[] glyphs, int width, int height)
    {
        _bitmap = bitmap;
        _glyphs = glyphs;
        _width = width;
        _height = height;
    }
}

/// <summary>
/// Persistent disk cache for baked font atlases. The cache stores the output of glyph
/// rasterization (atlas bitmap + glyph table) so subsequent loads of the same font skip
/// stb_truetype entirely. Cache entries are keyed by a hash of every baking input:
/// font file content, font size, padding, atlas dimensions, SDF flag and unicode ranges.
/// <para/>
/// The cache payload is Brotli-compressed (glyph coverage bitmaps compress to a small
/// fraction of the raw 64 MB atlas). Writes run on a background thread, go through a
/// temp file + atomic move so a crash never leaves a half-written entry, and are
/// best-effort: failures are swallowed and never break asset loading. Any corrupt or
/// stale entry is simply treated as a miss and overwritten on the next store.
/// </summary>
public sealed class FontAtlasCache
{
    private const int FormatVersion = 1;
    private const uint Magic = 0x31464341; // "ACF1" little-endian
    private const int HeaderSize = 32;
    private const int BrotliQuality = 5;

    private readonly string? _cacheDirectory;
    private readonly Lock _writeLock = new();
    private readonly HashSet<string> _pendingWrites = [];

    /// <summary>
    /// Creates the cache rooted at <paramref name="cacheDirectory"/>; a null directory disables caching.
    /// </summary>
    public FontAtlasCache(string? cacheDirectory)
    {
        if (cacheDirectory != null)
        {
            _cacheDirectory = cacheDirectory;
            Directory.CreateDirectory(cacheDirectory);
        }
    }

    public bool IsEnabled => _cacheDirectory != null;

    /// <summary>
    /// Tries to load a cached atlas baked with exactly these inputs.
    /// Returns null on a miss or when the entry is corrupt.
    /// </summary>
    public FontAtlasCacheEntry? TryLoad(
        ReadOnlySpan<byte> ttf,
        float fontSize,
        int padding,
        int atlasWidth,
        int atlasHeight,
        bool generateSdf,
        ReadOnlySpan<int2> unicodeRanges)
    {
        if (_cacheDirectory == null || ttf.IsEmpty)
        {
            return null;
        }

        string path = Path.Combine(_cacheDirectory, BuildCacheFileName(ttf, fontSize, padding, atlasWidth, atlasHeight, generateSdf, unicodeRanges));
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return ReadEntry(path);
        }
        catch
        {
            // Corrupt entry: treat as a miss, the next store overwrites it.
            return null;
        }
    }

    /// <summary>
    /// Asynchronously writes a baked atlas to the cache. The inputs and payload are
    /// captured synchronously; compression and file IO run on a background thread.
    /// The returned task completes when the entry is durable; the asset loader discards it.
    /// </summary>
    public Task StoreAsync(
        ReadOnlySpan<byte> ttf,
        float fontSize,
        int padding,
        int atlasWidth,
        int atlasHeight,
        bool generateSdf,
        ReadOnlySpan<int2> unicodeRanges,
        ReadOnlySpan<byte> bitmap,
        GlyphInfo[] glyphs)
    {
        if (_cacheDirectory == null)
        {
            return Task.CompletedTask;
        }

        string fileName = BuildCacheFileName(ttf, fontSize, padding, atlasWidth, atlasHeight, generateSdf, unicodeRanges);
        string path = Path.Combine(_cacheDirectory, fileName);

        lock (_writeLock)
        {
            if (!_pendingWrites.Add(fileName))
            {
                return Task.CompletedTask;
            }
        }

        // Capture managed copies now: the caller's native bitmap and glyph array
        // (mutated later by SDF adjustment) do not outlive CreateAsset.
        byte[] bitmapCopy = bitmap.ToArray();
        GlyphInfo[] glyphCopy = (GlyphInfo[])glyphs.Clone();

        return Task.Factory.StartNew(() =>
        {
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                WriteEntry(path, bitmapCopy, glyphCopy, atlasWidth, atlasHeight);
            }
            catch
            {
                // Best-effort: a failed cache write must never affect asset loading.
            }
            finally
            {
                lock (_writeLock)
                {
                    _pendingWrites.Remove(fileName);
                }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private FontAtlasCacheEntry ReadEntry(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        if (file.Length < HeaderSize)
        {
            throw new InvalidDataException("Truncated cache header");
        }

        ReadOnlySpan<byte> header = file.AsSpan(0, HeaderSize);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic ||
            BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4)) != FormatVersion)
        {
            throw new InvalidDataException("Unknown cache format");
        }

        ulong payloadHash = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(8));
        int width = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(16));
        int height = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(20));
        int glyphCount = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(24));
        int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(28));

        if (width <= 0 || height <= 0 || glyphCount < 0 || HeaderSize + compressedLength != file.Length)
        {
            throw new InvalidDataException("Corrupt cache header");
        }

        int bitmapLength = width * height;
        int glyphLength = glyphCount * Unsafe.SizeOf<GlyphInfo>();
        byte[] payload = new byte[bitmapLength + glyphLength];

        if (!BrotliDecoder.TryDecompress(file.AsSpan(HeaderSize), payload, out int written) || written != payload.Length)
        {
            throw new InvalidDataException("Brotli payload mismatch");
        }

        if (XxHash3.HashToUInt64(payload) != payloadHash)
        {
            throw new InvalidDataException("Payload hash mismatch");
        }

        byte[] bitmap = new byte[bitmapLength];
        payload.AsSpan(0, bitmapLength).CopyTo(bitmap);
        GlyphInfo[] glyphs = MemoryMarshal.Cast<byte, GlyphInfo>(payload.AsSpan(bitmapLength)).ToArray();

        return new FontAtlasCacheEntry(bitmap, glyphs, width, height);
    }

    private static void WriteEntry(string path, byte[] bitmap, GlyphInfo[] glyphs, int width, int height)
    {
        ReadOnlySpan<byte> glyphBytes = MemoryMarshal.AsBytes<GlyphInfo>(glyphs.AsSpan());

        byte[] payload = new byte[bitmap.Length + glyphBytes.Length];
        bitmap.AsSpan().CopyTo(payload.AsSpan(0, bitmap.Length));
        glyphBytes.CopyTo(payload.AsSpan(bitmap.Length));

        byte[] compressed = new byte[payload.Length + 1024];
        if (!BrotliEncoder.TryCompress(payload, compressed, out int compressedLength, BrotliQuality, 22))
        {
            throw new InvalidDataException("Brotli compression failed");
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(4), FormatVersion);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(8), XxHash3.HashToUInt64(payload));
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(20), height);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(24), glyphs.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(28), compressedLength);

        // Write through a temp file + atomic move so a crash mid-write can never
        // leave a half-written entry that a later load would treat as valid.
        string tempPath = path + ".tmp";
        using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
        {
            stream.Write(header);
            stream.Write(compressed, 0, compressedLength);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// Builds the cache file name from every baking input. The font bytes are hashed by
    /// content (not by file name) so editing, moving or duplicating a font invalidates
    /// nothing unless its rasterized output would actually change.
    /// </summary>
    private static string BuildCacheFileName(
        ReadOnlySpan<byte> ttf,
        float fontSize,
        int padding,
        int atlasWidth,
        int atlasHeight,
        bool generateSdf,
        ReadOnlySpan<int2> unicodeRanges)
    {
        ulong fontHash = XxHash3.HashToUInt64(ttf);
        ulong metaHash = ComputeMetaHash(fontSize, padding, atlasWidth, atlasHeight, generateSdf, unicodeRanges);
        return $"{fontHash:X16}_{metaHash:X16}.bin";
    }

    private static ulong ComputeMetaHash(
        float fontSize,
        int padding,
        int atlasWidth,
        int atlasHeight,
        bool generateSdf,
        ReadOnlySpan<int2> unicodeRanges)
    {
        // Little-endian fixed-layout metadata blob: [version][fontSize][padding][width][height][sdf][rangeCount][ranges]
        Span<byte> meta = stackalloc byte[25 + unicodeRanges.Length * Unsafe.SizeOf<int2>()];
        BinaryPrimitives.WriteInt32LittleEndian(meta, FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(meta.Slice(4), BitConverter.SingleToInt32Bits(fontSize));
        BinaryPrimitives.WriteInt32LittleEndian(meta.Slice(8), padding);
        BinaryPrimitives.WriteInt32LittleEndian(meta.Slice(12), atlasWidth);
        BinaryPrimitives.WriteInt32LittleEndian(meta.Slice(16), atlasHeight);
        meta[20] = generateSdf ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(meta.Slice(21), unicodeRanges.Length);
        MemoryMarshal.AsBytes<int2>(unicodeRanges).CopyTo(meta.Slice(25));

        return XxHash3.HashToUInt64(meta);
    }
}
