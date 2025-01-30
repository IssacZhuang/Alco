namespace Alco.Rendering;

public class PlantTileItem<TUserData> : BaseTileItem<PlantTileData, TUserData>
{
    public PlantTileItem(string name, PlantTileData tileData, TUserData userData, params ReadOnlySpan<Texture2D> textures) : base(name, tileData, userData, textures)
    {
    }
}