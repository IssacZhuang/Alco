using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public TiledTerrainBlock2D<TUserData> CreateTiledTerrainBlock2D<TUserData>(
        TileSet<TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
    )
    {
        return new TiledTerrainBlock2D<TUserData>(this, tileSet, material, width, height, name);
    }

    public TileSet<TUserData> CreateTileSet<TUserData>(
        Material material,
        TileSetParams<TUserData> @params,
        string name = "tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new TileSet<TUserData>(this, @params, material, sampler, name);
    }

    public TileSet<TUserData> CreateTileSet<TUserData>(
        Material material,
        TileSetParams<TUserData> @params,
        FilterMode filterMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new TileSet<TUserData>(this, @params, material, sampler, name);
    }

    public TileSet<TUserData> CreateTileSet<TUserData>(
        Material material,
        TileSetParams<TUserData> @params,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new TileSet<TUserData>(this, @params, material, sampler, name);
    }

    public TileSet<TUserData> CreateTileSet<TUserData>(
        Material material,
        TileSetParams<TUserData> @params,
        GPUSampler sampler,
        string name = "tile_set"
    )
    {
        return new TileSet<TUserData>(this, @params, material, sampler, name);
    }

}