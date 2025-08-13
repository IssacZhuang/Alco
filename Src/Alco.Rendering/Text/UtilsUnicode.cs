namespace Alco.Rendering;

public static class UtilsUnicode
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

    public static IEnumerable<int2> GetUnicodeRanges(FontLanguage language)
    {
        if (language.HasFlag(FontLanguage.Basic))
        {
            yield return RangeBasicLatin;
            yield return RangeLatin1Supplement;
            yield return RangeLatinExtendedA;
            yield return RangeCyrillic;
            yield return RangeCyrillicSupplement;
        }

        if (language.HasFlag(FontLanguage.Chinese))
        {
            yield return RangeCjkSymbolsAndPunctuation;
            yield return RangeCjkUnifiedIdeographs;
        }

        if (language.HasFlag(FontLanguage.Japanese))
        {
            yield return RangeHiragana;
            yield return RangeKatakana;
        }

        if (language.HasFlag(FontLanguage.Korean))
        {
            yield return RangeHangulCompatibilityJamo;
            yield return RangeHangulSyllables;
        }
    }
}
