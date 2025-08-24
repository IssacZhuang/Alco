using System.Text;

public class FontCharsetGenerator : BaseGenerator
{
    public override string OutputFolder => "Src/Alco.Engine/Assets/Fonts";

    public override void Generate()
    {
        var ranges = new[]
        {
            new { Start = 0x0020, End = 0x007F },  // Basic Latin
            new { Start = 0x00A0, End = 0x00FF },  // Latin-1 Supplement  
            new { Start = 0x0100, End = 0x017F },  // Latin Extended-A
            new { Start = 0x0400, End = 0x04FF },  // Cyrillic
            new { Start = 0x0370, End = 0x03FF },  // Greek
            new { Start = 0x3040, End = 0x309F },  // Hiragana
            new { Start = 0x30A0, End = 0x30FF },  // Katakana
            new { Start = 0x4E00, End = 0x9FFF },  // CJK Unified Ideographs
            new { Start = 0x3000, End = 0x303F },  // CJK Symbols and Punctuation
            new { Start = 0xAC00, End = 0xD7AF },  // Hangul Syllables
            new { Start = 0x3130, End = 0x318F }   // Hangul Compatibility Jamo
        };

        StringBuilder content = new StringBuilder();
        
        foreach (var range in ranges)
        {
            content.Append("\"");
            for (int i = range.Start; i <= range.End; i++)
            {
                if (char.IsControl((char)i) && i != 0x0020) // Skip control chars except space
                    continue;
                    
                char c = (char)i;
                if (c == '"' || c == '\\')
                {
                    content.Append('\\').Append(c);
                }
                else
                {
                    content.Append(c);
                }
            }
            content.AppendLine("\",");
        }

        WriteFile("msdfgen-charset.txt", content.ToString());
    }
}