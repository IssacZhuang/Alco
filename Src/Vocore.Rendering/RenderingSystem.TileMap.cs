namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public TiledTerrainBlock2D CreateTiledTerrainBlock2D(
        TextureAtlas textureAtlas,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
    )
    {
        return new TiledTerrainBlock2D(this, textureAtlas, material, width, height, name);
    }

}