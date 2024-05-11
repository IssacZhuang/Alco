using Vocore.Rendering;

namespace Vocore.GUI;

public static class RenderingSystemExtension
{
    public static Canvas CreateCanvas(this RenderingSystem system, Shader shaderSprite, Shader shaderText, Font font)
    {
        return new Canvas(system, shaderSprite, shaderText, font);
    }
}