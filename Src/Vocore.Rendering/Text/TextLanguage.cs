namespace Vocore.Rendering;

[Flags]
public enum TextLanguage
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
}
