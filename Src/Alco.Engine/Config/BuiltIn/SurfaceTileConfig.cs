using System.Numerics;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Configuration for surface tiles in the game engine.
/// Defines appearance, texturing, and blending properties for tiles.
/// </summary>
public class TileConfig : Configable, IValidatableConfig
{
    /// <summary>
    /// The display name of the tile.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// List of texture file paths used by this tile.
    /// </summary>
    public List<string> Textures { get; set; } = new();

    /// <summary>
    /// Color tint applied to the tile (RGBA).
    /// </summary>
    public ColorFloat Color { get; set; } = ColorFloat.White;

    /// <summary>
    /// Scale factor applied to the tile mesh.
    /// </summary>
    public Vector2 MeshScale { get; set; } = Vector2.One;

    /// <summary>
    /// Scale factor applied to the tile's UV coordinates.
    /// </summary>
    public Vector2 UvScale { get; set; } = Vector2.One;

    /// <summary>
    /// The position offset affected by the height.
    /// </summary>
    public Vector2 HeightOffsetFactor { get; set; } = Vector2.UnitY;

    /// <summary>
    /// Priority value used when blending multiple tiles.
    /// Higher values take precedence during blending operations.
    /// </summary>
    public float BlendPriority { get; set; } = 0.0f;

    /// <summary>
    /// Factor controlling the strength of blending between tiles.
    /// </summary>
    public float BlendFactor { get; set; } = 0.35f;

    /// <summary>
    /// Factor controlling the smoothness of tile edges during blending.
    /// </summary>
    public float EdgeSmoothFactor { get; set; } = 0.15f;

    /// <summary>
    /// Asynchronously loads all textures defined in the Textures list.
    /// </summary>
    /// <param name="assetSystem">The asset system used to load textures.</param>
    /// <returns>An array of tasks that will complete when textures are loaded.</returns>
    public Task<Texture2D>[] GetTexturesTasks(AssetSystem assetSystem)
    {
        Task<Texture2D>[] tasks = new Task<Texture2D>[Textures.Count];
        for (int i = 0; i < Textures.Count; i++)
        {
            tasks[i] = assetSystem.LoadAsync<Texture2D>(Textures[i]);
        }
        return tasks;
    }

    /// <summary>
    /// Synchronously loads all textures defined in the Textures list.
    /// </summary>
    /// <param name="assetSystem">The asset system used to load textures.</param>
    /// <returns>An array of loaded texture objects.</returns>
    public Texture2D[] GetTextures(AssetSystem assetSystem)
    {
        Texture2D[] textures = new Texture2D[Textures.Count];
        Task<Texture2D>[] tasks = GetTexturesTasks(assetSystem);
        Task.WaitAll(tasks);
        for (int i = 0; i < Textures.Count; i++)
        {
            textures[i] = tasks[i].Result;
        }
        return textures;
    }

    /// <summary>
    /// Validates the configuration by checking if all texture files exist.
    /// </summary>
    /// <param name="engine">The game engine instance.</param>
    /// <returns>A collection of validation error messages, if any.</returns>
    public IEnumerable<string> Validate(GameEngine engine)
    {
        AssetSystem assetSystem = engine.Assets;
        foreach (string texture in Textures)
        {
            if (!assetSystem.IsFileExist(texture))
            {
                yield return $"Texture file not found: '{texture}' in config '{Id}'";
            }
        }
    }
}
