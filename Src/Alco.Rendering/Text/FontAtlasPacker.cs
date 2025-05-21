using System.Numerics;
using StbTrueTypeSharp;
using static StbTrueTypeSharp.StbTrueType;

namespace Alco.Rendering;

/// <summary>
/// The atlas packer is used to create a font atlas and unicode to glyph mapping from a ttf font file. <br/>
/// Can be used to create <see cref="Font"/> instances.
/// </summary> 
public sealed unsafe class FontAtlasPacker : AutoDisposable
{
    private static readonly int MaxArrayLength = 0xD7AF + 1;
    private readonly stbtt_pack_context _context;
    private readonly GlyphInfo[] _glyphs;
    private readonly NativeBuffer<byte> _bitmap;
    private readonly int _width;
    private readonly int _height;

    public GlyphInfo[] Glyphs => _glyphs;
    public ReadOnlySpan<byte> Bitmap => _bitmap.AsReadOnlySpan();
    public int Width => _width;
    public int Height => _height;

    public FontAtlasPacker(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _width = width;
        _height = height;

        _bitmap = new NativeBuffer<byte>(width * height);
        _glyphs = new GlyphInfo[MaxArrayLength];
        _context = new stbtt_pack_context();

        stbtt_PackBegin(_context, _bitmap.UnsafePointer, width, height, width, 1, null);
    }

    public void Add(ReadOnlySpan<byte> ttf, float fontSize, IEnumerable<int2> unicodeRanges)
    {
        if (ttf == ReadOnlySpan<byte>.Empty)
        {
            throw new ArgumentNullException(nameof(ttf));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize);
        ArgumentNullException.ThrowIfNull(unicodeRanges);

        if (!TryCreateFont(ttf, 0, out stbtt_fontinfo font))
        {
            throw new InvalidOperationException("Failed to create font from ttf data");
        }

        float invWidth = 1 / (float)_width;
        float invHeight = 1 / (float)_height;
        float invfontSize = 1 / (float)fontSize;

        float scale = stbtt_ScaleForPixelHeight(font, fontSize);
        int ascent, descent, lineGap;
        stbtt_GetFontVMetrics(font, &ascent, &descent, &lineGap);

        foreach (var range in unicodeRanges)
        {
            if (range.X < 0 || range.Y > MaxArrayLength || range.X > range.Y)
            {
                continue;
            }

            //stbtt_packedchar[] packedchar = new stbtt_packedchar[range.y - range.x + 1];
            NativeBuffer<stbtt_packedchar> packedchars = new NativeBuffer<stbtt_packedchar>(range.Y - range.X + 1);

            stbtt_PackFontRange(_context, font.data, 0, fontSize,
                    range.X,
                    range.Y - range.X + 1,
                    packedchars.UnsafePointer);




            for (int i = 0; i < packedchars.Length; ++i)
            {
                stbtt_packedchar packedchar = packedchars[i];

                GlyphInfo glyphInfo = new GlyphInfo
                {
                    UVRect = new Vector4(packedchar.x0 * invWidth, packedchar.y0 * invHeight, (packedchar.x1 - packedchar.x0) * invWidth, (packedchar.y1 - packedchar.y0) * invHeight),
                    Size = new Vector2((packedchar.x1 - packedchar.x0) * invfontSize, (packedchar.y1 - packedchar.y0) * invfontSize),
                    Offset = new Vector2(packedchar.xoff * invfontSize, -packedchars[i].yoff * invfontSize-0.25f), // -0.25 to match the true type mesh in the engine 
                    Advance = packedchar.xadvance * invfontSize
                };

                _glyphs[i + range.X] = glyphInfo;
            }

            packedchars.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        //dispose native resources
        _bitmap.Dispose();
        stbtt_PackEnd(_context);
    }
}