using Alco.Graphics;

namespace Alco.Rendering;

// tile map factory

public partial class RenderingSystem
{
    public SurfaceTileBlock2D<TUserData> CreateSurfaceBlock2D<TUserData>(
        SurfaceTileSet<TUserData> tileSet,
        TileMapHeightBuffer heightData,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
    )
    {
        return new SurfaceTileBlock2D<TUserData>(
            this,
            tileSet,
            heightData,
            material,
            width,
            height,
            name);
    }


    public SurfaceTileSet<TUserData> CreateSurfaceTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<SurfaceTileData, TUserData>> items,
        string name = "tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new SurfaceTileSet<TUserData>(this, items, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateSurfaceTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<SurfaceTileData, TUserData>> items,
        FilterMode filterMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new SurfaceTileSet<TUserData>(this, items, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateSurfaceTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<SurfaceTileData, TUserData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new SurfaceTileSet<TUserData>(this, items, material, sampler, name);
    }

    public SurfaceTileSet<TUserData> CreateSurfaceTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<SurfaceTileData, TUserData>> items,
        GPUSampler sampler,
        string name = "tile_set"
    )
    {
        return new SurfaceTileSet<TUserData>(this, items, material, sampler, name);
    }


    public WaterTileBlock2D<TUserData> CreateWaterTileBlock2D<TUserData>(
        WaterTileSet<TUserData> tileSet,
        TileMapHeightBuffer surfaceHeightData,
        Material material,
        int width,
        int height,
        string name = "water_tile_block_2d"
    )
    {
        return new WaterTileBlock2D<TUserData>(
            this,
            tileSet,
            surfaceHeightData,
            material,
            width,
            height,
            name);
    }

    public WaterTileSet<TUserData> CreateWaterTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<WaterTileData, TUserData>> items,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new WaterTileSet<TUserData>(this, items, material, sampler, name);
    }

    public WaterTileSet<TUserData> CreateWaterTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<WaterTileData, TUserData>> items,
        FilterMode filterMode,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new WaterTileSet<TUserData>(this, items, material, sampler, name);
    }

    public WaterTileSet<TUserData> CreateWaterTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<WaterTileData, TUserData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new WaterTileSet<TUserData>(this, items, material, sampler, name);
    }

    public WaterTileSet<TUserData> CreateWaterTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<WaterTileData, TUserData>> items,
        GPUSampler sampler,
        string name = "water_tile_set"
    )
    {
        return new WaterTileSet<TUserData>(this, items, material, sampler, name);
    }

    public PlantTileBlock2D<TUserData> CreatePlantTileBlock2D<TUserData>(
        PlantTileSet<TUserData> tileSet,
        TileMapHeightBuffer heightData,
        Material material,
        int width,
        int height,
        string name = "plant_tile_block_2d"
    )

    {
        return new PlantTileBlock2D<TUserData>(
            this,
            tileSet,
            heightData,
            material,
            width,
            height, name);
    }

    public PlantTileSet<TUserData> CreatePlantTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<PlantTileData, TUserData>> items,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new PlantTileSet<TUserData>(this, items, material, sampler, name);
    }

    public PlantTileSet<TUserData> CreatePlantTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<PlantTileData, TUserData>> items,
        FilterMode filterMode,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new PlantTileSet<TUserData>(this, items, material, sampler, name);
    }

    public PlantTileSet<TUserData> CreatePlantTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<PlantTileData, TUserData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new PlantTileSet<TUserData>(this, items, material, sampler, name);
    }

    public PlantTileSet<TUserData> CreatePlantTileSet<TUserData>(
        Material material,
        IReadOnlyList<BaseTileItem<PlantTileData, TUserData>> items,
        GPUSampler sampler,
        string name = "plant_tile_set"
    )
    {
        return new PlantTileSet<TUserData>(this, items, material, sampler, name);
    }

    public TileMapHeightBuffer CreateTileMapHeightBuffer(int width, int height, string name = "tile_map_height_buffer")
    {
        return new TileMapHeightBuffer(this, width, height, name);
    }

    public FloodFillLightMap CreateTileLightMap(ComputeMaterial material, int width, int height, string name = "tile_light_map")
    {
        return new FloodFillLightMap(this, material, width, height, name);
    }

    public ConnectableTileBlock2D CreateConnectableTileBlock2D(int width, int height, string name = "connectable_tile_block_2d")
    {
        return new ConnectableTileBlock2D(this, width, height, name);
    }


}