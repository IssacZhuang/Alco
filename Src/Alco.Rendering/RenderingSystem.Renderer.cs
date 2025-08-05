namespace Alco.Rendering;

using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

// renderer factory

public partial class RenderingSystem
{
    /// <summary>
    /// Create a text renderer that uses a render context and a material.
    /// </summary>
    /// <param name="renderContext">The render context to use.</param>
    /// <param name="material">The material to use.</param>
    /// <param name="name">The name of the renderer.</param>
    /// <returns>The created text renderer.</returns>
    public TextRenderer CreateTextRenderer(IRenderContext renderContext, Material material, string name = "text_renderer")
    {
        return new TextRenderer(this, renderContext, MeshTrueType, material, name);
    }

    public SpriteRenderer CreateSpriteRenderer(IRenderContext renderContext, Material material, string name = "sprite_renderer")
    {
        return new SpriteRenderer(this, renderContext, MeshCenteredSprite, material, name);
    }

    public RenderContext CreateRenderContext(string name = "render_context")
    {
        return new RenderContext(this, name);
    }

    public SubRenderContext CreateSubRenderContext(string name = "sub_render_context")
    {
        return new SubRenderContext(this, name);
    }

    /// <summary>
    /// Create a dynamic mesh renderer that manages multiple DynamicMesh instances.
    /// </summary>
    /// <param name="renderContext">The render context to use.</param>
    /// <param name="vertexBufferSizePerChunk">The size of each vertex buffer in bytes. Default is 64KB.</param>
    /// <param name="indexBufferSizePerChunk">The size of each index buffer in bytes. Default is 16KB.</param>
    /// <param name="name">The name of the renderer.</param>
    /// <returns>The created dynamic mesh renderer.</returns>
    public DynamicMeshRenderer CreateDynamicMeshRenderer(IRenderContext renderContext,
        uint vertexBufferSizePerChunk = 64 * 1024, uint indexBufferSizePerChunk = 16 * 1024, string name = "dynamic_mesh_renderer")
    {
        return new DynamicMeshRenderer(this, renderContext, vertexBufferSizePerChunk, indexBufferSizePerChunk, name);
    }

    /// <summary>
    /// Creates a high-performance instance renderer for batching and rendering multiple instances of the same type.
    /// </summary>
    /// <typeparam name="T">The unmanaged type representing instance data.</typeparam>
    /// <param name="renderContext">The render context for command submission.</param>
    /// <param name="material">The material to use for rendering instances.</param>
    /// <param name="instanceBufferShaderName">The shader resource name for the instance buffer. Default is "_instances".</param>
    /// <param name="sizePerBuffer">The size of each GPU buffer in bytes. Default is 256KB.</param>
    /// <param name="name">The name of the renderer.</param>
    /// <returns>The created instance renderer.</returns>
    public InstanceRenderer<T> CreateInstanceRenderer<T>(IRenderContext renderContext, Material material,
        string instanceBufferShaderName = ShaderResourceId.Instances,
        int sizePerBuffer = 256 * 1024, string name = "instance_renderer") where T : unmanaged
    {
        return new InstanceRenderer<T>(this, renderContext, material, instanceBufferShaderName, sizePerBuffer, name);
    }

    /// <summary>
    /// Creates a tile renderer for rendering tile-based graphics with efficient batching.
    /// </summary>
    /// <param name="renderContext">The render context to use for rendering.</param>
    /// <param name="tileSet">The tile set containing tile items and materials.</param>
    /// <param name="width">The width of the tile map in tiles.</param>
    /// <param name="height">The height of the tile map in tiles.</param>
    /// <param name="name">The name of the tile renderer.</param>
    /// <returns>The created tile renderer.</returns>
    public TileRenderer CreateTileRenderer(IRenderContext renderContext, TileSet tileSet, int width, int height, string name = "tile_renderer")
    {
        return new TileRenderer(this, renderContext, tileSet, width, height, 64, 64, name);
    }

    /// <summary>
    /// Creates a tile renderer for rendering tile-based graphics with efficient batching and custom batch sizes.
    /// </summary>
    /// <param name="renderContext">The render context to use for rendering.</param>
    /// <param name="tileSet">The tile set containing tile items and materials.</param>
    /// <param name="width">The width of the tile map in tiles.</param>
    /// <param name="height">The height of the tile map in tiles.</param>
    /// <param name="batchSizeX">The width of each batch in tiles.</param>
    /// <param name="batchSizeY">The height of each batch in tiles.</param>
    /// <param name="name">The name of the tile renderer.</param>
    /// <returns>The created tile renderer.</returns>
    public TileRenderer CreateTileRenderer(IRenderContext renderContext, TileSet tileSet, int width, int height, int batchSizeX, int batchSizeY, string name = "tile_renderer")
    {
        return new TileRenderer(this, renderContext, tileSet, width, height, batchSizeX, batchSizeY, name);
    }
}