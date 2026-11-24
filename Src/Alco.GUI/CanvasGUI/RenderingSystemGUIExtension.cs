using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>Extension methods for creating <see cref="Canvas"/> instances from a <see cref="RenderingSystem"/>.</summary>
public static class RenderingSystemGUIExtension
{
    /// <summary>Creates a bindless UI <see cref="Canvas"/> on <paramref name="system"/>.</summary>
    /// <param name="system">Target rendering system.</param>
    /// <param name="inputTracker">Input tracker for pointer and text input events.</param>
    /// <param name="defaultFont">Default font asset used by child text nodes.</param>
    /// <param name="size">Optional initial canvas logical dimensions.</param>
    /// <param name="glyphCache">Optional shared SDF glyph cache; when <see langword="null"/>, the canvas creates and owns a private instance.</param>
    /// <param name="shaders">Optional shader system override used to resolve UI materials; defaults to <see cref="RenderingSystem.Shaders"/>.</param>
    /// <returns>The created <see cref="Canvas"/>.</returns>
    public static Canvas CreateCanvas(
        this RenderingSystem system,
        IUIInputTracker inputTracker,
        Font defaultFont,
        Vector2? size = null,
        GlyphCache? glyphCache = null,
        ShaderSystem? shaders = null)
    {
        return new Canvas(system, inputTracker, defaultFont, size, glyphCache, shaders);
    }
}
