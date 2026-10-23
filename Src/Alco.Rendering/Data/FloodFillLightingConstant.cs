using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Constants for the flood-fill lighting compute pass over a region.
/// </summary>
public struct FloodFillLightingConstant
{
    public int2 RectOrigin;
    public int2 RectSize;
    public float AttenuationSide;
    public float AttenuationCorner;
}
