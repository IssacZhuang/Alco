using Alco.Graphics;

namespace Alco.Rendering;

// tile map factory

public partial class RenderingSystem
{
    public SurfaceTileBlock2D CreateSurfaceBlock2D(
        SurfaceTileSet tileSet,
        TileMapHeightBuffer heightData,
        Material material,
        int width,
        int height,
        string name = "tiled_terrain_block_2d"
    )
    {
        return new SurfaceTileBlock2D(
            this,
            tileSet,
            heightData,
            material,
            width,
            height,
            name);
    }


    public SurfaceTileSet CreateSurfaceTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<SurfaceTileData>> items,
        string name = "tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new SurfaceTileSet(this, items, material, sampler, name);
    }

    public SurfaceTileSet CreateSurfaceTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<SurfaceTileData>> items,
        FilterMode filterMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new SurfaceTileSet(this, items, material, sampler, name);
    }

    public SurfaceTileSet CreateSurfaceTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<SurfaceTileData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new SurfaceTileSet(this, items, material, sampler, name);
    }

    public SurfaceTileSet CreateSurfaceTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<SurfaceTileData>> items,
        GPUSampler sampler,
        string name = "tile_set"
    )
    {
        return new SurfaceTileSet(this, items, material, sampler, name);
    }


    public WaterTileBlock2D CreateWaterTileBlock2D(
        WaterTileSet tileSet,
        TileMapHeightBuffer surfaceHeightData,
        Material material,
        int width,
        int height,
        string name = "water_tile_block_2d"
    )
    {
        return new WaterTileBlock2D(
            this,
            tileSet,
            surfaceHeightData,
            material,
            width,
            height,
            name);
    }

    public WaterTileSet CreateWaterTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<WaterTileData>> items,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new WaterTileSet(this, items, material, sampler, name);
    }

    public WaterTileSet CreateWaterTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<WaterTileData>> items,
        FilterMode filterMode,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new WaterTileSet(this, items, material, sampler, name);
    }

    public WaterTileSet CreateWaterTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<WaterTileData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new WaterTileSet(this, items, material, sampler, name);
    }

    public WaterTileSet CreateWaterTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<WaterTileData>> items,
        GPUSampler sampler,
        string name = "water_tile_set"
    )
    {
        return new WaterTileSet(this, items, material, sampler, name);
    }

    public PlantTileBlock2D CreatePlantTileBlock2D(
        PlantTileSet tileSet,
        TileMapHeightBuffer heightData,
        Material material,
        int width,
        int height,
        string name = "plant_tile_block_2d"
    )

    {
        return new PlantTileBlock2D(
            this,
            tileSet,
            heightData,
            material,
            width,
            height, name);
    }

    public PlantTileSet CreatePlantTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new PlantTileSet(this, items, material, sampler, name);
    }

    public PlantTileSet CreatePlantTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        FilterMode filterMode,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new PlantTileSet(this, items, material, sampler, name);
    }

    public PlantTileSet CreatePlantTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new PlantTileSet(this, items, material, sampler, name);
    }

    public PlantTileSet CreatePlantTileSet(
        Material material,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        GPUSampler sampler,
        string name = "plant_tile_set"
    )
    {
        return new PlantTileSet(this, items, material, sampler, name);
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