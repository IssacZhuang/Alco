namespace Alco.Rendering;

public class PlantTileItem : BaseTileItem<PlantTileData>
{
    public PlantTileItem(string name, PlantTileData tileData, object? userData, params ReadOnlySpan<Texture2D> textures) : base(name, tileData, userData, textures)
    {
    }
}