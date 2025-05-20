
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

public class ConnectableTileData
{
    private static readonly Rect[] _connectedSprites = new Rect[16];

    static ConnectableTileData()
    {
        for (int i = 0; i < 16; i++)
        {
            _connectedSprites[i] = UtilsUV.CalculateFrameUVRect(4, 4, i);
        }
    }

    public Material Material { get; }
    public Vector2 Size { get; }
    public Vector2 Offset { get; }
    public ColorFloat LightMapOpacity { get; }
    public object? UserData { get; }

    public ConnectableTileData(Material material, object? userData)
    {
        ArgumentNullException.ThrowIfNull(material);
        UserData = userData;
        Material = material;
        Size = new Vector2(1, 1);
        Offset = new Vector2(0, 0);
    }

    public ConnectableTileData(Material material, Vector2 size, Vector2 offset, object? userData)
    {
        ArgumentNullException.ThrowIfNull(material);
        UserData = userData;
        Material = material;
        Size = size;
        Offset = offset;
        LightMapOpacity = new ColorFloat(1, 1, 1, 1);
    }
    
    public ConnectableTileData(Material material, Vector2 size, Vector2 offset, ColorFloat lightMapOpacity, object? userData)
    {
        ArgumentNullException.ThrowIfNull(material);
        UserData = userData;
        Material = material;
        Size = size;
        Offset = offset;
        LightMapOpacity = lightMapOpacity;
    }

    public static Rect GetConnectUVRect(int index)
    {
        return _connectedSprites[index];
    }
    
}