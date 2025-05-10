
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
    public object? UserData { get; }

    public ConnectableTileData(Material material, object? userData)
    {
        ArgumentNullException.ThrowIfNull(material);
        UserData = userData;
        Material = material;
    }

    public static Rect GetConnectUVRect(int index)
    {
        return _connectedSprites[index];
    }
    
}