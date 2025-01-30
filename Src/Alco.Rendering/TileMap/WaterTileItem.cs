namespace Alco.Rendering;

public class WaterTileItem<TUserData> : BaseTileItem<WaterTileData, TUserData>
{
    public WaterTileItem(string name, WaterTileData tileData, TUserData userData, params ReadOnlySpan<Texture2D> textures) : base(name, tileData, userData, textures)
    {
    }
}