namespace Vocore.Rendering;

using System.Numerics;
using System.Runtime.CompilerServices;

public partial class RenderingSystem
{
    public TextRenderer CreateTextRenderer(ICamera camera, Shader shader)
    {
        return new TextRenderer(this, MeshTrueType, camera, shader);
    }

    public SpriteRenderer CreateSpriteRenderer(ICamera camera, Shader shader)
    {
        return new SpriteRenderer(_device, MeshSprite, camera, shader);
    }

    public MaterialRenderer CreateMaterialRenderer()
    {
        return new MaterialRenderer(_device);
    }

    public CanvasRenderer CreateCanvasRenderer(ICamera camera, Shader shaderText, Shader shaderSprite)
    {
        return new CanvasRenderer(this, camera, shaderText, shaderSprite);
    }

}