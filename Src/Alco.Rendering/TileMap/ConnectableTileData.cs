
namespace Alco.Rendering;

public class ConnectableTileData
{
    private readonly Sprite[] _connectedSprites;

    public object? UserData;

    public ConnectableTileData(Texture2D texture, object? userData)
    {
        UserData = userData;
        _connectedSprites = new Sprite[16];
        for (int i = 0; i < 16; i++)
        {
            Rect uvRect = UtilsUV.CalculateFrameUVRect(4, 4, i);
            _connectedSprites[i] = new Sprite($"sprite_{i}", texture, uvRect);
        }
    }

    public Sprite GetConnectedSprite(int index)
    {
        return _connectedSprites[index];
    }

    public Sprite GetConnectedSprite(ConnectDirection direction)
    {
        return _connectedSprites[(int)direction];
    }

}