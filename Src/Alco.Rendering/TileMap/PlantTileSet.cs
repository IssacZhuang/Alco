using Alco.Graphics;

namespace Alco.Rendering;

public class PlantTileSet<TUserData> : BaseTileSet2<PlantTileData, TUserData>
{
    internal PlantTileSet(
        RenderingSystem renderingSystem, 
        IReadOnlyList<BaseTileItem<PlantTileData, TUserData>> items, 
        Material material, 
        GPUSampler sampler, 
        string name
    ) : base(renderingSystem, items, material, sampler, name)
    {
    }


}