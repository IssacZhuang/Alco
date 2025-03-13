namespace Alco.Rendering;

using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

public partial class RenderingSystem
{
    public TextRenderer CreateTextRenderer(GraphicsBuffer camera, Shader shader)
    {
        return new TextRenderer(this, MeshTrueType, camera, shader);
    }

    /// <summary>
    /// Create a text renderer that uses a render context and a material.
    /// </summary>
    /// <param name="renderContext">The render context to use.</param>
    /// <param name="material">The material to use.</param>
    /// <param name="name">The name of the renderer.</param>
    /// <returns>The created text renderer.</returns>
    public TextRenderer2 CreateTextRenderer2(RenderContext renderContext, Material material, string name = "text_renderer")
    {
        return new TextRenderer2(this, renderContext, MeshTrueType, material, name);
    }

    public SpriteRenderer CreateSpriteRenderer(GraphicsBuffer camera, Shader shader)
    {
        return new SpriteRenderer(this, MeshCenteredSprite, camera, shader);
    }

    public RenderContext CreateMaterialRenderer()
    {
        return new RenderContext(this);
    }

    public CanvasRenderer CreateCanvasRenderer(GraphicsBuffer camera, Shader shaderSprite, Shader shaderText)
    {
        return new CanvasRenderer(this, camera, shaderSprite, shaderText);
    }
}