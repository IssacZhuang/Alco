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

    public TextRenderer2 CreateTextRenderer2(Material material, string name = "text_renderer")
    {
        return new TextRenderer2(this, MeshTrueType, material, name);
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