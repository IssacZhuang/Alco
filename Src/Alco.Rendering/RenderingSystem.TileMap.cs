using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Tile map factory methods for the rendering system.
/// Provides methods to create various tile-related objects including surface, water, and plant tile sets and blocks.
/// </summary>
public partial class RenderingSystem
{
    /// <summary>
    /// Creates a 2D surface tile block for rendering terrain surfaces.
    /// </summary>
    /// <param name="tileSet">The tile set containing surface tile data and textures.</param>
    /// <param name="heightData">Height buffer data for the terrain surface.</param>
    /// <param name="material">Material used for rendering the surface tiles.</param>
    /// <param name="width">Width of the tile block in tiles.</param>
    /// <param name="height">Height of the tile block in tiles.</param>
    /// <param name="name">Optional name for the tile block (default: "tiled_terrain_block_2d").</param>
    /// <returns>A new SurfaceTileBlock2D instance.</returns>
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

    /// <summary>
    /// Creates a surface tile set with default linear clamp sampling.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of surface tile items to include in the set.</param>
    /// <param name="name">Optional name for the tile set (default: "tile_set").</param>
    /// <returns>A new SurfaceTileSet instance.</returns>
    public SurfaceTileSet CreateSurfaceTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<SurfaceTileData>> items,
        string name = "tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new SurfaceTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a surface tile set with specified filter mode and clamp to edge addressing.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of surface tile items to include in the set.</param>
    /// <param name="filterMode">Texture filtering mode to use.</param>
    /// <param name="name">Optional name for the tile set (default: "tile_set").</param>
    /// <returns>A new SurfaceTileSet instance.</returns>
    public SurfaceTileSet CreateSurfaceTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<SurfaceTileData>> items,
        FilterMode filterMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new SurfaceTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a surface tile set with specified filter mode and address mode.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of surface tile items to include in the set.</param>
    /// <param name="filterMode">Texture filtering mode to use.</param>
    /// <param name="addressMode">Texture addressing mode to use.</param>
    /// <param name="name">Optional name for the tile set (default: "tile_set").</param>
    /// <returns>A new SurfaceTileSet instance.</returns>
    public SurfaceTileSet CreateSurfaceTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<SurfaceTileData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "tile_set"
    ){
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new SurfaceTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a surface tile set with a custom GPU sampler.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of surface tile items to include in the set.</param>
    /// <param name="sampler">Custom GPU sampler to use for texture sampling.</param>
    /// <param name="name">Optional name for the tile set (default: "tile_set").</param>
    /// <returns>A new SurfaceTileSet instance.</returns>
    public SurfaceTileSet CreateSurfaceTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<SurfaceTileData>> items,
        GPUSampler sampler,
        string name = "tile_set"
    )
    {
        return new SurfaceTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a 2D water tile block for rendering water surfaces.
    /// </summary>
    /// <param name="tileSet">The tile set containing water tile data and textures.</param>
    /// <param name="surfaceHeightData">Height buffer data for the underlying surface.</param>
    /// <param name="material">Material used for rendering the water tiles.</param>
    /// <param name="width">Width of the tile block in tiles.</param>
    /// <param name="height">Height of the tile block in tiles.</param>
    /// <param name="name">Optional name for the tile block (default: "water_tile_block_2d").</param>
    /// <returns>A new WaterTileBlock2D instance.</returns>
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

    /// <summary>
    /// Creates a water tile set with default linear clamp sampling.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of water tile items to include in the set.</param>
    /// <param name="name">Optional name for the tile set (default: "water_tile_set").</param>
    /// <returns>A new WaterTileSet instance.</returns>
    public WaterTileSet CreateWaterTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<WaterTileData>> items,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new WaterTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a water tile set with specified filter mode and clamp to edge addressing.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of water tile items to include in the set.</param>
    /// <param name="filterMode">Texture filtering mode to use.</param>
    /// <param name="name">Optional name for the tile set (default: "water_tile_set").</param>
    /// <returns>A new WaterTileSet instance.</returns>
    public WaterTileSet CreateWaterTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<WaterTileData>> items,
        FilterMode filterMode,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new WaterTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a water tile set with specified filter mode and address mode.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of water tile items to include in the set.</param>
    /// <param name="filterMode">Texture filtering mode to use.</param>
    /// <param name="addressMode">Texture addressing mode to use.</param>
    /// <param name="name">Optional name for the tile set (default: "water_tile_set").</param>
    /// <returns>A new WaterTileSet instance.</returns>
    public WaterTileSet CreateWaterTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<WaterTileData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "water_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new WaterTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a water tile set with a custom GPU sampler.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of water tile items to include in the set.</param>
    /// <param name="sampler">Custom GPU sampler to use for texture sampling.</param>
    /// <param name="name">Optional name for the tile set (default: "water_tile_set").</param>
    /// <returns>A new WaterTileSet instance.</returns>
    public WaterTileSet CreateWaterTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<WaterTileData>> items,
        GPUSampler sampler,
        string name = "water_tile_set"
    )
    {
        return new WaterTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a 2D plant tile block for rendering vegetation and plant surfaces.
    /// </summary>
    /// <param name="tileSet">The tile set containing plant tile data and textures.</param>
    /// <param name="heightData">Height buffer data for the terrain surface.</param>
    /// <param name="material">Material used for rendering the plant tiles.</param>
    /// <param name="width">Width of the tile block in tiles.</param>
    /// <param name="height">Height of the tile block in tiles.</param>
    /// <param name="name">Optional name for the tile block (default: "plant_tile_block_2d").</param>
    /// <returns>A new PlantTileBlock2D instance.</returns>
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

    /// <summary>
    /// Creates a plant tile set with default linear clamp sampling.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of plant tile items to include in the set.</param>
    /// <param name="name">Optional name for the tile set (default: "plant_tile_set").</param>
    /// <returns>A new PlantTileSet instance.</returns>
    public PlantTileSet CreatePlantTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.SamplerLinearClamp;
        return new PlantTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a plant tile set with specified filter mode and clamp to edge addressing.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of plant tile items to include in the set.</param>
    /// <param name="filterMode">Texture filtering mode to use.</param>
    /// <param name="name">Optional name for the tile set (default: "plant_tile_set").</param>
    /// <returns>A new PlantTileSet instance.</returns>
    public PlantTileSet CreatePlantTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        FilterMode filterMode,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, AddressMode.ClampToEdge);
        return new PlantTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a plant tile set with specified filter mode and address mode.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of plant tile items to include in the set.</param>
    /// <param name="filterMode">Texture filtering mode to use.</param>
    /// <param name="addressMode">Texture addressing mode to use.</param>
    /// <param name="name">Optional name for the tile set (default: "plant_tile_set").</param>
    /// <returns>A new PlantTileSet instance.</returns>
    public PlantTileSet CreatePlantTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        FilterMode filterMode,
        AddressMode addressMode,
        string name = "plant_tile_set"
    )
    {
        GPUSampler sampler = _device.GetSampler(filterMode, addressMode);
        return new PlantTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a plant tile set with a custom GPU sampler.
    /// </summary>
    /// <param name="atlasPackingMaterial">Material used for packing textures into an atlas.</param>
    /// <param name="items">List of plant tile items to include in the set.</param>
    /// <param name="sampler">Custom GPU sampler to use for texture sampling.</param>
    /// <param name="name">Optional name for the tile set (default: "plant_tile_set").</param>
    /// <returns>A new PlantTileSet instance.</returns>
    public PlantTileSet CreatePlantTileSet(
        Material atlasPackingMaterial,
        IReadOnlyList<BaseTileItem<PlantTileData>> items,
        GPUSampler sampler,
        string name = "plant_tile_set"
    )
    {
        return new PlantTileSet(this, items, atlasPackingMaterial, sampler, name);
    }

    /// <summary>
    /// Creates a height buffer for tile maps to store elevation data.
    /// </summary>
    /// <param name="width">Width of the height buffer in pixels.</param>
    /// <param name="height">Height of the height buffer in pixels.</param>
    /// <param name="name">Optional name for the height buffer (default: "tile_map_height_buffer").</param>
    /// <returns>A new TileMapHeightBuffer instance.</returns>
    public TileMapHeightBuffer CreateTileMapHeightBuffer(int width, int height, string name = "tile_map_height_buffer")
    {
        return new TileMapHeightBuffer(this, width, height, name);
    }

    /// <summary>
    /// Creates a flood fill light map for tile-based lighting calculations.
    /// </summary>
    /// <param name="material">Compute material used for the lighting calculations.</param>
    /// <param name="width">Width of the light map in pixels.</param>
    /// <param name="height">Height of the light map in pixels.</param>
    /// <param name="name">Optional name for the light map (default: "tile_light_map").</param>
    /// <returns>A new FloodFillLightMap instance.</returns>
    public FloodFillLightMap CreateTileLightMap(ComputeMaterial material, int width, int height, string name = "tile_light_map")
    {
        return new FloodFillLightMap(this, material, width, height, name);
    }

    /// <summary>
    /// Creates a 2D connectable tile block for tiles that can connect to adjacent tiles.
    /// </summary>
    /// <param name="width">Width of the tile block in tiles.</param>
    /// <param name="height">Height of the tile block in tiles.</param>
    /// <param name="name">Optional name for the tile block (default: "connectable_tile_block_2d").</param>
    /// <returns>A new ConnectableTileBlock2D instance.</returns>
    public ConnectableTileBlock2D CreateConnectableTileBlock2D(int width, int height, string name = "connectable_tile_block_2d")
    {
        return new ConnectableTileBlock2D(this, width, height, name);
    }

}