using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public SurfaceTiledBlock2D<TUserData> CreateTiledTerrainBlock2D<TUserData>(
        SurfaceTileSet<TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
    )
    {
        return new SurfaceTiledBlock2D<TUserData>(this, tileSet, material, width, height, name);
    }

    public SurfaceTileSet<TUserData> CreateTileSet<TUserData>(
        Material material,
        SurfaceTileSetParams<TUserData> @params,
        string name = "tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new SurfaceTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateTileSet<TUserData>(
        Material material,
        SurfaceTileSetParams<TUserData> @params,
        FilterMode filterMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new SurfaceTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateTileSet<TUserData>(
        Material material,
        SurfaceTileSetParams<TUserData> @params,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new SurfaceTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateTileSet<TUserData>(
        Material material,
        SurfaceTileSetParams<TUserData> @params,
        GPUSampler sampler,
        string name = "tile_set"
    )
    {
        return new SurfaceTileSet<TUserData>(this, @params, material, sampler, name);
    }

}