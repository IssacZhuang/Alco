using System.Numerics;

namespace Vocore.Engine;

public struct GlyphInfo
{
    /// <summary>
    /// The normalized position of the glyph in the font texture. Can be used as UV coordinates.
    /// </summary>
    public Vector2 Position;
    /// <summary>
    /// The normalized size of the glyph in the font texture. Can be used as UV coordinates.
    /// </summary>
    public Vector2 Size;
    /// <summary>
    /// The normalized offset of the glyph from the baseline.
    /// </summary>
    public Vector2 Offset;
    /// <summary>
    /// The normalized advance relative to the font size.
    /// </summary>
    public float Advance;
}
