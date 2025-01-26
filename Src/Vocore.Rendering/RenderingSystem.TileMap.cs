using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public SurfaceTileBlock2D<TUserData> CreateSurfaceBlock2D<TUserData>(
        SurfaceTileSet<TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
    )
    {
        return new SurfaceTileBlock2D<TUserData>(this, tileSet, material, width, height, name);
    }

    public SurfaceTileSet<TUserData> CreateSurfaceTileSet<TUserData>(
        Material material,
        SurfaceTileSetParams<TUserData> @params,
        string name = "tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new SurfaceTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateSurfaceTileSet<TUserData>(
        Material material,
        SurfaceTileSetParams<TUserData> @params,
        FilterMode filterMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new SurfaceTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateSurfaceTileSet<TUserData>(
        Material material,
        SurfaceTileSetParams<TUserData> @params,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new SurfaceTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateSurfaceTileSet<TUserData>(
        Material material,
        SurfaceTileSetParams<TUserData> @params,
        GPUSampler sampler,
        string name = "tile_set"
    )
    {
        return new SurfaceTileSet<TUserData>(this, @params, material, sampler, name);
    }


    public WaterTileBlock2D<TUserData> CreateWaterTileBlock2D<TUserData>(
        WaterTileSet<TUserData> tileSet,
        Material material,
        int width,
        int height,
        string name = "water_tile_block_2d"
    )
    {
        return new WaterTileBlock2D<TUserData>(this, tileSet, material, width, height, name);
    }

    public WaterTileSet<TUserData> CreateWaterTileSet<TUserData>(
        Material material,
        WaterTileSetParams<TUserData> @params,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new WaterTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public WaterTileSet<TUserData> CreateWaterTileSet<TUserData>(
        Material material,
        WaterTileSetParams<TUserData> @params,
        FilterMode filterMode,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new WaterTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public WaterTileSet<TUserData> CreateWaterTileSet<TUserData>(
        Material material,
        WaterTileSetParams<TUserData> @params,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new WaterTileSet<TUserData>(this, @params, material, sampler, name);
    }

    public WaterTileSet<TUserData> CreateWaterTileSet<TUserData>(
        Material material,
        WaterTileSetParams<TUserData> @params,
        GPUSampler sampler,
        string name = "water_tile_set"
    )
    {
        return new WaterTileSet<TUserData>(this, @params, material, sampler, name);
    }

}