namespace Alco.Rendering;

using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

public partial class RenderingSystem
{
    public OldTextRenderer CreateOldTextRenderer(GraphicsBuffer camera, Shader shader)
    {
        return new OldTextRenderer(this, MeshTrueType, camera, shader);
    }

    /// <summary>
    /// Create a text renderer that uses a render context and a material.
    /// </summary>
    /// <param name="renderContext">The render context to use.</param>
    /// <param name="material">The material to use.</param>
    /// <param name="name">The name of the renderer.</param>
    /// <returns>The created text renderer.</returns>
    public TextRenderer CreateTextRenderer(RenderContext renderContext, Material material, string name = "text_renderer")
    {
        return new TextRenderer(this, renderContext, MeshTrueType, material, name);
    }

    public OldSpriteRenderer CreateOldSpriteRenderer(GraphicsBuffer camera, Shader shader)
    {
        return new OldSpriteRenderer(this, MeshCenteredSprite, camera, shader);
    }

    public SpriteRenderer CreateSpriteRenderer(RenderContext renderContext, Material material, string name = "sprite_renderer")
    {
        return new SpriteRenderer(this, renderContext, MeshCenteredSprite, material, name);
    }

    public RenderContext CreateRenderContext()
    {
        return new RenderContext(this);
    }

    public CanvasRenderer CreateCanvasRenderer(GraphicsBuffer camera, Shader shaderSprite, Shader shaderText)
    {
        return new CanvasRenderer(this, camera, shaderSprite, shaderText);
    }

    public DynamicMeshRenderer CreateDynamicMeshRenderer(RenderContext renderContext, string name = "dynamic_mesh_renderer")
    {
        return new DynamicMeshRenderer(this, renderContext, name);
    }
}