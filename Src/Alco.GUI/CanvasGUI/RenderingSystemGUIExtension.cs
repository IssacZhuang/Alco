using Alco.Rendering;

namespace Alco.GUI;

/// <summary>Creates canvas UI renderers backed by a rendering system.</summary>
public static class RenderingSystemGUIExtension
{
    /// <summary>
    /// Creates a canvas using caller-provided pass templates and a default UI surface.
    /// A supplied render context remains owned by its creator; otherwise the canvas creates and owns one.
    /// </summary>
    /// <param name="system">The rendering system providing GPU resources and the shared material compiler.</param>
    /// <param name="inputTracker">The canvas input source.</param>
    /// <param name="spriteTemplate">The IUISurface pass template for images and masks.</param>
    /// <param name="textTemplate">The IUISurface glyph pass template with premultiplied-alpha output.</param>
    /// <param name="defaultSurface">The UI surface used for ordinary drawing and assets without an explicit surface.</param>
    /// <param name="defaultFont">The default font used by text nodes.</param>
    /// <param name="renderContext">An optional externally owned render context.</param>
    /// <returns>A ready-to-use canvas.</returns>
    public static Canvas CreateCanvas(
        this RenderingSystem system,
        IUIInputTracker inputTracker,
        ShaderLibrary spriteTemplate,
        ShaderLibrary textTemplate,
        ShaderLibrary defaultSurface,
        Font defaultFont,
        RenderContext? renderContext = null)
    {
        return new Canvas(system, inputTracker, spriteTemplate, textTemplate, defaultSurface, defaultFont, renderContext);
    }
}
