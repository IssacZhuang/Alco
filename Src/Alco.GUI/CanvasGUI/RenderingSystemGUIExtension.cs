using Alco.Rendering;

namespace Alco.GUI;

public static class RenderingSystemGUIExtension
{
    public static Canvas CreateCanvas(
        this RenderingSystem system, 
        IUIInputTracker inputTracker, 
        Material defaultSpriteMaterial, 
        Material defaultTextMaterial,
        Font defaultFont
        )
    {
        return new Canvas(system, inputTracker, defaultSpriteMaterial, defaultTextMaterial, defaultFont);
    }

}