namespace Alco.Rendering;

[Flags]
public enum FontLanguage
{
    /// <summary>
    /// The basic language set for english, numbers and symbols. <br/>
    /// Unicode range: <br/>
    /// - Basic Latin (0000-007F), <br/>
    /// - Latin-1 Supplement (0080-00FF), <br/>
    /// - Latin Extended-A (0100-017F), <br/>
    /// - Cyrillic (0400-04FF), <br/>
    /// - Cyrillic Supplement (0500-052F), <br/>
    /// </summary>
    Basic = 1 << 0,
    /// <summary>
    /// The language for Chinese <br/>
    /// Unicode range: <br/>
    /// - CJK Symbols and Punctuation (3000-303F), <br/>
    /// - CJK Unified Ideographs (4E00-9FFF), <br/>
    /// </summary>
    Chinese = 1 << 1,
    /// <summary>
    /// The language for Japanese <br/>
    /// Unicode range: <br/>
    /// - Hiragana (3040-309F), <br/>
    /// - Katakana (30A0-30FF), <br/>
    /// </summary>
    Japanese = 1 << 2,
    /// <summary>
    /// The language for Korean <br/>
    /// Unicode range: <br/>
    /// - Hangul Compatibility Jamo (3130-318F), <br/>
    /// - Hangul Syllables (AC00-D7AF), <br/>
    /// </summary>
    Korean = 1 << 3
    ,
    /// <summary>
    /// The language for Cyrillic scripts.
    /// Unicode range:
    /// - Cyrillic (0400-04FF),
    /// - Cyrillic Supplement (0500-052F)
    /// </summary>
    Cyrillic = 1 << 4,
    /// <summary>
    /// The language for Greek.
    /// Unicode range:
    /// - Greek and Coptic (0370-03FF)
    /// </summary>
    Greek = 1 << 5,
    /// <summary>
    /// The language for Thai.
    /// Unicode range:
    /// - Thai (0E00-0E7F)
    /// </summary>
    Thai = 1 << 6,
    /// <summary>
    /// The language for Vietnamese.
    /// This corresponds to ImGui's Vietnamese range, which includes Latin
    /// characters with Vietnamese-specific diacritics.
    /// </summary>
    Vietnamese = 1 << 7
}
