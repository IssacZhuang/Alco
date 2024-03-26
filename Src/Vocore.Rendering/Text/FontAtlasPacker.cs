using System.Numerics;
using StbTrueTypeSharp;
using static StbTrueTypeSharp.StbTrueType;

namespace Vocore.Rendering;

/// <summary>
/// The atlas packer is used to create a font atlas and unicode to glyph mapping from a ttf font file. <br/>
/// Can be used to create <see cref="Font"/> instances.
/// </summary> 
public unsafe class FontAtlasPacker : IDisposable
{
    private static readonly int MaxArrayLength = 0xD7AF + 1;
    private readonly stbtt_pack_context _context;
    private readonly GlyphInfo[] _glyphs;
    private readonly NativeBuffer<byte> _bitmap;
    private readonly int _width;
    private readonly int _height;

    public GlyphInfo[] Glyphs => _glyphs;

    public FontAtlasPacker(int width, int height)
    {

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        _width = width;
        _height = height;

        _bitmap = new NativeBuffer<byte>(width * height);
        _glyphs = new GlyphInfo[MaxArrayLength];
        _context = new stbtt_pack_context();
        stbtt_PackBegin(_context, _bitmap.UnsafePointer, width, height, width, 1, null);
    }

    public void Add(ReadOnlySpan<byte> ttf, float fontSize, IEnumerable<int2> unicodeRanges)
    {
        if (ttf == null)
        {
            throw new ArgumentNullException(nameof(ttf));
        }

        if (fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        if (unicodeRanges == null)
        {
            throw new ArgumentNullException(nameof(unicodeRanges));
        }

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
            if (range.x < 0 || range.y > MaxArrayLength || range.x > range.y)
            {
                continue;
            }

            //stbtt_packedchar[] packedchar = new stbtt_packedchar[range.y - range.x + 1];
            NativeBuffer<stbtt_packedchar> packedchars = new NativeBuffer<stbtt_packedchar>(range.y - range.x + 1);

            stbtt_PackFontRange(_context, font.data, 0, fontSize,
                    range.x,
                    range.y - range.x + 1,
                    packedchars.UnsafePointer);




            for (int i = 0; i < packedchars.Length; ++i)
            {
                float yOff = packedchars[i].yoff;
                //yOff += ascent * scale;

                stbtt_packedchar packedchar = packedchars[i];

                GlyphInfo glyphInfo = new GlyphInfo
                {
                    UVRect = new Vector4(packedchar.x0 * invWidth, packedchar.y0 * invHeight, (packedchar.x1 - packedchar.x0) * invWidth, (packedchar.y1 - packedchar.y0) * invHeight),
                    Size = new Vector2((packedchar.x1 - packedchar.x0) * invfontSize, (packedchar.y1 - packedchar.y0) * invfontSize),
                    Offset = new Vector2(packedchar.xoff * invfontSize, -yOff * invfontSize),
                    Advance = packedchar.xadvance * invfontSize
                };

                _glyphs[i + range.x] = glyphInfo;
            }

            packedchars.Dispose();
        }
    }

    public Font Build()
    {
        return new Font(_bitmap.MemoryRef, _width, _height, _glyphs);
    }

    public void Dispose()
    {
        _bitmap.Dispose();
        stbtt_PackEnd(_context);
        GC.SuppressFinalize(this);
    }
}