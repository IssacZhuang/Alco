namespace Alco.Rendering;

public class WaterTileItem : BaseTileItem<WaterTileData>
{
    public WaterTileItem(string name, WaterTileData tileData, object? userData, params ReadOnlySpan<Texture2D> textures) : base(name, tileData, userData, textures)
    {
    }
}