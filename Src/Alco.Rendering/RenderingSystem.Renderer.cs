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
}