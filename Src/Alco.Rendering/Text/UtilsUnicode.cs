namespace Alco.Rendering;

public static class UtilsUnicode
{
    /// <summary>
    /// Basic Latin characters including ASCII printable characters, digits, and common punctuation.
    /// Used by English and most Western European languages in their basic form.
    /// </summary>
    public static readonly int2 RangeBasicLatin = new int2(0x0020, 0x007F);

    /// <summary>
    /// Latin-1 Supplement containing accented characters and symbols for Western European languages.
    /// Includes characters for French, German, Spanish, Italian, Portuguese, and other Western European languages.
    /// </summary>
    public static readonly int2 RangeLatin1Supplement = new int2(0x00A0, 0x00FF);

    /// <summary>
    /// Latin Extended-A containing additional Latin characters for Central European languages.
    /// Includes characters for Polish, Czech, Slovak, Hungarian, Romanian, Croatian, and other Central European languages.
    /// </summary>
    public static readonly int2 RangeLatinExtendedA = new int2(0x0100, 0x017F);

    /// <summary>
    /// Latin Extended-B containing extended Latin characters for various European and African languages.
    /// Includes characters for Estonian, Latvian, Lithuanian, Turkish, and some African languages using Latin script.
    /// </summary>
    public static readonly int2 RangeLatinExtendedB = new int2(0x0180, 0x024F);

    /// <summary>
    /// Combining Diacritical Marks used to add accents and diacritics to base characters.
    /// These are non-spacing marks that combine with preceding characters to form accented letters.
    /// </summary>
    public static readonly int2 RangeCombiningDiacriticalMarks = new int2(0x0300, 0x036F);

    /// <summary>
    /// Cyrillic script used for Slavic languages including Russian, Ukrainian, Bulgarian, and Serbian.
    /// Also used for some non-Slavic languages in former Soviet Union countries.
    /// </summary>
    public static readonly int2 RangeCyrillic = new int2(0x0400, 0x04FF);

    /// <summary>
    /// Cyrillic Supplement containing additional Cyrillic characters for minority languages.
    /// Includes characters for languages like Abkhazian, Ossetic, and other Caucasian languages using Cyrillic.
    /// </summary>
    public static readonly int2 RangeCyrillicSupplement = new int2(0x0500, 0x052F);

    /// <summary>
    /// Hebrew script used for Hebrew and Yiddish languages.
    /// Written from right to left, primarily used in Israel and Jewish communities worldwide.
    /// </summary>
    public static readonly int2 RangeHebrew = new int2(0x0590, 0x05FF);

    /// <summary>
    /// Arabic script used for Arabic, Persian, Urdu, and many other languages.
    /// Written from right to left, used across the Middle East, North Africa, and parts of Asia.
    /// </summary>
    public static readonly int2 RangeArabic = new int2(0x0600, 0x06FF);

    /// <summary>
    /// Thai script used for the Thai language in Thailand.
    /// An abugida script with complex character combinations and tone markers.
    /// </summary>
    public static readonly int2 RangeThai = new int2(0x0E00, 0x0E7F);

    /// <summary>
    /// Tibetan script used for Tibetan language and related languages in Tibet, Bhutan, and surrounding regions.
    /// An abugida script derived from ancient Indian scripts.
    /// </summary>
    public static readonly int2 RangeTibetan = new int2(0x0F00, 0x0FFF);

    /// <summary>
    /// Hiragana syllabary used for Japanese language, primarily for native Japanese words and grammatical elements.
    /// One of three writing systems used in Japanese alongside Katakana and Kanji.
    /// </summary>
    public static readonly int2 RangeHiragana = new int2(0x3040, 0x309F);

    /// <summary>
    /// Katakana syllabary used for Japanese language, primarily for foreign words and onomatopoeia.
    /// One of three writing systems used in Japanese alongside Hiragana and Kanji.
    /// </summary>
    public static readonly int2 RangeKatakana = new int2(0x30A0, 0x30FF);

    /// <summary>
    /// Greek and Coptic script used for the Greek language and historical Coptic language.
    /// Includes both modern Greek characters and ancient Greek variations.
    /// </summary>
    public static readonly int2 RangeGreek = new int2(0x0370, 0x03FF);

    /// <summary>
    /// CJK Symbols and Punctuation containing common punctuation marks for Chinese, Japanese, and Korean.
    /// Includes ideographic space, punctuation marks, and symbols shared across CJK languages.
    /// </summary>
    public static readonly int2 RangeCjkSymbolsAndPunctuation = new int2(0x3000, 0x303F);

    /// <summary>
    /// CJK Unified Ideographs containing the most commonly used Chinese characters (Hanzi/Kanji/Hanja).
    /// Shared by Chinese, Japanese, and Korean languages, representing the core set of ideographic characters.
    /// </summary>
    public static readonly int2 RangeCjkUnifiedIdeographs = new int2(0x4E00, 0x9FFF);

    /// <summary>
    /// CJK Unified Ideographs Extension A containing less commonly used Chinese characters.
    /// Includes rare characters, classical Chinese characters, and regional variants used in Chinese, Japanese, and Korean.
    /// </summary>
    public static readonly int2 RangeCjkUnifiedIdeographsExtensionA = new int2(0x3400, 0x4DBF);

    /// <summary>
    /// Halfwidth and Fullwidth Forms containing fullwidth ASCII characters and punctuation marks.
    /// Includes fullwidth versions of Latin letters, digits, and punctuation used in CJK typography (：；，。？！etc.).
    /// </summary>
    public static readonly int2 RangeHalfwidthAndFullwidthForms = new int2(0xFF00, 0xFFEF);

    /// <summary>
    /// CJK Compatibility Forms containing compatibility variants of CJK punctuation and symbols.
    /// Includes alternative forms of punctuation marks used in vertical text and traditional typography.
    /// </summary>
    public static readonly int2 RangeCjkCompatibilityForms = new int2(0xFE30, 0xFE4F);

    /// <summary>
    /// Vertical Forms containing punctuation variants specifically designed for vertical text layout.
    /// Used in traditional Chinese, Japanese, and Korean vertical writing systems.
    /// </summary>
    public static readonly int2 RangeVerticalForms = new int2(0xFE10, 0xFE1F);

    /// <summary>
    /// Hangul Compatibility Jamo containing Korean alphabet components for compatibility purposes.
    /// Includes individual consonants and vowels (jamo) used to construct Korean syllables (Hangul).
    /// </summary>
    public static readonly int2 RangeHangulCompatibilityJamo = new int2(0x3130, 0x318F);

    /// <summary>
    /// Devanagari script used for Hindi, Sanskrit, Marathi, and other Indo-Aryan languages.
    /// An abugida script widely used in India and Nepal.
    /// </summary>
    public static readonly int2 RangeDevanagari = new int2(0x0900, 0x097F);

    /// <summary>
    /// Hangul Syllables containing precomposed Korean syllable blocks.
    /// Each syllable is formed by combining consonant and vowel jamo, used for modern Korean writing.
    /// </summary>
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
            yield return RangeCjkUnifiedIdeographsExtensionA;
            yield return RangeHalfwidthAndFullwidthForms;
            yield return RangeCjkCompatibilityForms;
            yield return RangeVerticalForms;
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
