namespace Alco.Rendering;

public sealed class SurfaceTileItem<TUserData> : BaseTileItem<SurfaceTileData, TUserData>
{
    public SurfaceTileItem(string name, SurfaceTileData tileData, TUserData userData, params ReadOnlySpan<Texture2D> textures) : base(name, tileData, userData, textures)
    {
    }
}