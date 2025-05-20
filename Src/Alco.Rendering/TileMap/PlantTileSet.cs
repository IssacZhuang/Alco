using Alco.Graphics;

namespace Alco.Rendering;

public class PlantTileSet : BaseTileSet<PlantTileData>
{
    internal PlantTileSet(
        RenderingSystem renderingSystem,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        Material material,
        GPUSampler sampler,
        string name
    ) : base(renderingSystem, items, material, sampler, name)
    {
    }


}