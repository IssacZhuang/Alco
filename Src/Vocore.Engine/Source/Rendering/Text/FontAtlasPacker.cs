using StbTrueTypeSharp;
using static StbTrueTypeSharp.StbTrueType;

namespace Vocore.Engine;

public unsafe class FontAtlasPacker : IDisposable
{
    public static readonly int2 RangeBasicLatin = new int2(0x0020, 0x007F);
    public static readonly int2 RangeLatin1Supplement = new int2(0x00A0, 0x00FF);
    public static readonly int2 RangeLatinExtendedA = new int2(0x0100, 0x017F);
    public static readonly int2 RangeLatinExtendedB = new int2(0x0180, 0x024F);
    public static readonly int2 RangeCombiningDiacriticalMarks = new int2(0x0300, 0x036F);
    public static readonly int2 RangeCyrillic = new int2(0x0400, 0x04FF);
    public static readonly int2 RangeCyrillicSupplement = new int2(0x0500, 0x052F);
    public static readonly int2 RangeHebrew = new int2(0x0590, 0x05FF);
    public static readonly int2 RangeArabic = new int2(0x0600, 0x06FF);
    public static readonly int2 RangeThai = new int2(0x0E00, 0x0E7F);
    public static readonly int2 RangeTibetan = new int2(0x0F00, 0x0FFF);
    public static readonly int2 RangeHiragana = new int2(0x3040, 0x309F);
    public static readonly int2 RangeKatakana = new int2(0x30A0, 0x30FF);
    public static readonly int2 RangeGreek = new int2(0x0370, 0x03FF);
    public static readonly int2 RangeCjkSymbolsAndPunctuation = new int2(0x3000, 0x303F);
    public static readonly int2 RangeCjkUnifiedIdeographs = new int2(0x4E00, 0x9FFF);
    public static readonly int2 RangeHangulCompatibilityJamo = new int2(0x3130, 0x318F);
    public static readonly int2 RangeDevanagari = new int2(0x0900, 0x097F);
    public static readonly int2 RangeHangulSyllables = new int2(0xAC00, 0xD7AF);

    private static readonly int MaxArrayLength = 0xD7AF;
    private readonly stbtt_pack_context _context;
    private readonly GlyphInfo[] _glyphs;
    private readonly NativeBuffer<byte> _bitmap;

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

        _bitmap = new NativeBuffer<byte>(width * height);
        _glyphs = new GlyphInfo[MaxArrayLength];
        _context = new stbtt_pack_context();
        stbtt_PackBegin(_context, _bitmap.UnsafePointer, width, height, width, 1, null);
    }

    public void Add(byte[] ttf, float fontSize, IEnumerable<int2> characterRanges)
    {
        if (ttf == null)
        {
            throw new ArgumentNullException(nameof(ttf));
        }

        if (fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        if (characterRanges == null)
        {
            throw new ArgumentNullException(nameof(characterRanges));
        }

        if (!TryCreateFont(ttf, 0, out stbtt_fontinfo font))
        {
            throw new InvalidOperationException("Failed to create font from ttf data");
        }


        float scale = stbtt_ScaleForPixelHeight(font, fontSize);
        int ascent, descent, lineGap;
        stbtt_GetFontVMetrics(font, &ascent, &descent, &lineGap);

        foreach (var range in characterRanges)
        {
            if (range.x < 0 || range.y > MaxArrayLength || range.x > range.y)
            {
                continue;
            }

            stbtt_packedchar[] packedchar = new stbtt_packedchar[range.y - range.x + 1];

            fixed (stbtt_packedchar* packedcharPtr = packedchar)
            {
                stbtt_PackFontRange(_context, font.data, 0, fontSize,
                        range.x,
                        range.y - range.x + 1,
                        packedcharPtr);
            }

            for (var i = 0; i < packedchar.Length; ++i)
            {
                var yOff = packedchar[i].yoff;
                yOff += ascent * scale;

                var glyphInfo = new GlyphInfo
                {
                    Position = new int2(packedchar[i].x0, packedchar[i].y0),
                    Size = new int2(packedchar[i].x1 - packedchar[i].x0, packedchar[i].y1 - packedchar[i].y0),
                    Offset = new int2((int)packedchar[i].xoff, (int)Math.Round(yOff)),
                    Advance = (int)Math.Round(packedchar[i].xadvance)
                };

                _glyphs[i + range.x] = glyphInfo;
            }
        }
    }

    public void WriteTexture(Texture2D texture)
    {
        if (texture == null)
        {
            throw new ArgumentNullException(nameof(texture));
        }

        
    }

    public void Dispose()
    {
        _bitmap.Dispose();
        stbtt_PackEnd(_context);
    }
}