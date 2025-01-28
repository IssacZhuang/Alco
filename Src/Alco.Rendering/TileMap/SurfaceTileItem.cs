namespace Alco.Rendering;

public sealed class SurfaceTileItem<TUserData> : BaseTileItem<SurfaceTileData, TUserData>
{
    public SurfaceTileItem(string name, SurfaceTileData tileData, TUserData userData) : base(name, tileData, userData)
    {
    }
}