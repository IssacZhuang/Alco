using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Tile map factory methods for the rendering system.
/// Provides methods to create various tile-related objects including surface, water, and plant tile sets and blocks.
/// </summary>
public partial class RenderingSystem
{
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