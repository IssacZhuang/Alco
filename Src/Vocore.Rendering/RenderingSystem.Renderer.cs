namespace Vocore.Rendering;

using System.Runtime.CompilerServices;

public partial class RenderingSystem
{
    public TextRenderer CreateTextRenderer(ICamera camera, Shader shader)
    {
        return new TextRenderer(_device, TrueTypeMesh, camera, shader);
    }

    public SpriteRenderer CreateSpriteRenderer(ICamera camera, Shader shader)
    {
        return new SpriteRenderer(_device, SpriteMesh, camera, shader);
    }
}