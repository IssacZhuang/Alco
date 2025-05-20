namespace Alco.Rendering;

public sealed class SurfaceTileItem : BaseTileItem<SurfaceTileData>
{
    public SurfaceTileItem(string name, SurfaceTileData tileData, object? userData, params ReadOnlySpan<Texture2D> textures) : base(name, tileData, userData, textures)
    {
    }
}