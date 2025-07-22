using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Tile map factory methods for the rendering system.
/// Provides methods to create various tile-related objects including surface, water, and plant tile sets and blocks.
/// </summary>
public partial class RenderingSystem
{
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
}