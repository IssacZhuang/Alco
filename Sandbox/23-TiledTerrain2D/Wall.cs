using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

public class Wall : IObstacle, IConnectableTile
{
    private static readonly Rect[] _connectedSprites = new Rect[16];

    static Wall()
    {
        for (int i = 0; i < 16; i++)
        {
            _connectedSprites[i] = UVUtility.CalculateFrameUVRect(4, 4, i);
        }
    }

    public int2 Position { get; set; }
    public Color32 Opacity { get; set; }

    public Material Material {get;}

    public Vector2 Size {get;}

    public Vector2 Offset {get;}

    public Wall(int2 position, Material material, Vector2 size, Vector2 offset, Color32 opacity)
    {
        Position = position;
        Material = material;
        Size = size;
        Offset = offset;
        Opacity = opacity;
    }

    public Rect GetConnectUVRect(int connectDirection)
    {
        return _connectedSprites[connectDirection];
    }
}


