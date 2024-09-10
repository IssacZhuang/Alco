using Vocore.Rendering;

namespace Vocore.GUI;

public static class RenderingSystemGUIExtension
{
    public static Canvas CreateCanvas(this RenderingSystem system, IUIInputTracker inputTracker, Shader shaderSprite, Shader shaderText)
    {
        return new Canvas(system, inputTracker, shaderSprite, shaderText);
    }

}