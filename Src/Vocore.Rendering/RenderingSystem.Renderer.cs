namespace Vocore.Rendering;

using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

public partial class RenderingSystem
{
    public TextRenderer CreateTextRenderer(GraphicsBuffer camera, Shader shader)
    {
        return new TextRenderer(this, MeshTrueType, camera, shader);
    }

    public SpriteRenderer CreateSpriteRenderer(GraphicsBuffer camera, Shader shader)
    {
        return new SpriteRenderer(this, MeshSprite, camera, shader);
    }

    public MaterialRenderer CreateMaterialRenderer()
    {
        return new MaterialRenderer(this);
    }

    public CanvasRenderer CreateCanvasRenderer(GraphicsBuffer camera, Shader shaderSprite, Shader shaderText)
    {
        return new CanvasRenderer(this, camera, shaderSprite, shaderText);
    }
}