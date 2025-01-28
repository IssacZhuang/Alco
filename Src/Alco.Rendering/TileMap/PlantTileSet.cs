using Alco.Graphics;

namespace Alco.Rendering;

public class PlantTileSet<TUserData> : BaseTileSet<PlantTileData, TUserData>
{
    internal PlantTileSet(RenderingSystem renderingSystem, BaseTileSetParams<PlantTileData, TUserData> @params, Material material, GPUSampler sampler, string name) : base(renderingSystem, @params, material, sampler, name)
    {
    }


}