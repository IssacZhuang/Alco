using System.Numerics;

namespace Alco.Rendering;

public struct PlantTileData : ITileData
{
    public Rect UVRect;
    public Vector4 Color;
    public Vector2 Scale;
    public Vector2 HeightOffsetFactor;
    public float HasContent;
    public float RandomOffsetFactor;
    public Vector2 _reserved;//for memory alignment
    

    public PlantTileData()
    {
        UVRect = new Rect(0, 0, 1, 1);
        Color = new Vector4(1, 1, 1, 1);
        Scale = new Vector2(1, 1);
        HeightOffsetFactor = new Vector2(0, 1);
        HasContent = 1;
        RandomOffsetFactor = 0.2f;
    }

    public void SetUVRect(Rect rect)
    {
        UVRect = rect;
    }
}