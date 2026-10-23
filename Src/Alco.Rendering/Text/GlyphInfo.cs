using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Atlas placement and metrics of a single glyph.
/// </summary>
public struct GlyphInfo
{
    public Vector4 UVRect;
    public Vector2 Offset;
    public Vector2 Size;
    public float Advance;

    /// <summary>
    /// Returns a readable representation of the glyph metrics.
    /// </summary>
    /// <returns>A string containing the UV rect, offset, size and advance.</returns>
    public override string ToString()
    {
        return $"UVRect: {UVRect}, Offset: {Offset}, Size: {Size}, Advance: {Advance}";
    }
}
