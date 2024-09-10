using Vocore.Rendering;

namespace Vocore.GUI;

public static class RenderingSystemGUIExtension
{
    public static Canvas CreateCanvas(this RenderingSystem system, Shader shaderSprite, Shader shaderText)
    {
        return new Canvas(system, shaderSprite, shaderText);
    }

}