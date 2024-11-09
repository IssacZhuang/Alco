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
        return new MaterialRenderer(_device);
    }

    public CanvasRenderer CreateCanvasRenderer(GraphicsBuffer camera, Shader shaderSprite, Shader shaderText)
    {
        return new CanvasRenderer(this, camera, shaderSprite, shaderText);
    }

    public BlitRenderer CreateBlitRenderer(Shader shaderBlit)
    {
        return new BlitRenderer(this, shaderBlit);
    }

    public ColorSpaceConverter CreateColorSpaceConverter(Shader shader)
    {
        return new ColorSpaceConverter(this, shader);
    }


    public Uncharted2ToneMap CreateUncharted2ToneMap(Shader shader)
    {
        return new Uncharted2ToneMap(this, shader);
    }

    public ReinhardLuminanceToneMap CreateReinhardLuminanceToneMap(Shader shader)
    {
        return new ReinhardLuminanceToneMap(this, shader);
    }

}