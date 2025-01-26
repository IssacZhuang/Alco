using System.Numerics;

namespace Alco.Rendering;

public struct GlyphInfo
{
    public Vector4 UVRect;
    public Vector2 Offset;
    public Vector2 Size;
    public float Advance;

    public override string ToString()
    {
        return $"UVRect: {UVRect}, Offset: {Offset}, Size: {Size}, Advance: {Advance}";
    }
}
