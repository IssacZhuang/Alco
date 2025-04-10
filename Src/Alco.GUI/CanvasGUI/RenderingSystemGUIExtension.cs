using Alco.Rendering;

namespace Alco.GUI;

public static class RenderingSystemGUIExtension
{
    public static Canvas CreateCanvas(this RenderingSystem system, IUIInputTracker inputTracker, Material defaultSpriteMaterial, Material defaultTextMaterial)
    {
        return new Canvas(system, inputTracker, defaultSpriteMaterial, defaultTextMaterial);
    }

}