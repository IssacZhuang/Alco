//per sprite in GPU
using System.Numerics;
using System.Runtime.InteropServices;
using Vocore;

[StructLayout(LayoutKind.Sequential)]
public struct SurfaceTileData
{
    public SurfaceTileData()
    {
        UVRect = new Rect(0, 0, 1, 1);
        MeshScale = Vector2.One;
        UVScale = Vector2.One;
        HeightOffsetFactor = Vector2.UnitY;
        BlendPriority = 0;
        BlendFactor = 0.35f;
        EdgeSmoothFactor = 0.15f;
    }
    public Rect UVRect;
    public Vector2 MeshScale;
    public Vector2 UVScale;
    /// <summary>
    /// The x,y offset affected by the height.
    /// </summary>
    public Vector2 HeightOffsetFactor;
    /// <summary>
    /// The tile of lower priority blend the texture from the tile of higher priority.
    /// </summary>
    public float BlendPriority;
    /// <summary>
    /// the blend factor of the tile.
    /// </summary>
    public float BlendFactor;
    /// <summary>
    /// The factor of the edge smoothing when the tile height is different from the neighbor tile.
    /// </summary>
    public float EdgeSmoothFactor;
    public Vector3 _reserved;//for memory alignment
}