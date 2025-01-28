using System.Numerics;

namespace Alco.Rendering;

public struct PlantTileData : ITileData
{
    public Rect UVRect;
    public Vector4 Color;
    public Vector2 Scale;
    public Vector2 HeightOffsetFactor;

    public PlantTileData()
    {
        UVRect = new Rect(0, 0, 1, 1);
        Color = new Vector4(1, 1, 1, 1);
        Scale = new Vector2(1, 1);
        HeightOffsetFactor = new Vector2(0, 0);
    }

    public void SetUVRect(Rect rect)
    {
        UVRect = rect;
    }
}