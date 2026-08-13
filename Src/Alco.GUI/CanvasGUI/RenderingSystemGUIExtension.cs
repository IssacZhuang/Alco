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

    /// <summary>
    /// Creates a canvas that records its passes into a shared (render graph owned)
    /// context instead of a standalone one. The context stays owned by its creator;
    /// the canvas does not dispose it.
    /// </summary>
    public static Canvas CreateCanvas(
        this RenderingSystem system,
        IUIInputTracker inputTracker,
        Material defaultSpriteMaterial,
        Material defaultTextMaterial,
        Font defaultFont,
        RenderContext renderContext
        )
    {
        return new Canvas(system, inputTracker, defaultSpriteMaterial, defaultTextMaterial, defaultFont, renderContext);
    }

}
