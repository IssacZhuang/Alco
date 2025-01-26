using Alco.Rendering;

namespace Alco.GUI;

public static class RenderingSystemGUIExtension
{
    public static Canvas CreateCanvas(this RenderingSystem system, IUIInputTracker inputTracker, Shader shaderSprite, Shader shaderText)
    {
        return new Canvas(system, inputTracker, shaderSprite, shaderText);
    }

}