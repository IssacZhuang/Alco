using System.Numerics;

namespace Alco.Rendering;

public struct FloodFillLightingConstant
{
    public int2 RectOrigin;
    public int2 RectSize;
    public float AttenuationSide;
    public float AttenuationCorner;
}
