using System.Numerics;
using System.Runtime.InteropServices;

namespace Vocore.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct WaterTileData : ITileData
{
    public WaterTileData()
    {
        UVRect = new Rect(0, 0, 1, 1);
        BlendPriority = 0;
        BlendFactor = 0.35f;
    }
    public Rect UVRect;
    public Vector4 Color;
    /// <summary>
    /// The tile of lower priority blend the texture from the tile of higher priority.
    /// </summary>
    public float BlendPriority;
    /// <summary>
    /// the blend factor of the tile.
    /// </summary>
    public float BlendFactor;
    public Vector2 _reserved;//for memory alignment
    public void SetUVRect(Rect rect)
    {
        UVRect = rect;
    }
}