using Alco.Graphics;

namespace Alco.GUI;

public partial class Canvas
{
    /// <summary>Standard premultiplied alpha blend state matching <c>AlcoGUI_Pass</c> shader output.</summary>
    public static BlendState PremultiplyBlend => UIBatchRenderer.PremultiplyBlend;

    /// <summary>Composes a custom <c>IUISurface</c> module resolvable through this canvas's <see cref="ShaderSystem"/> and configures premultiplied alpha blending.</summary>
    /// <param name="surfaceModule">Slang module name exporting an <c>IUISurface</c> implementation.</param>
    /// <param name="name">Optional diagnostic material name.</param>
    /// <returns>A newly created <see cref="Material"/> ready to assign to <see cref="UIImage"/>, <see cref="UIText"/>, or <see cref="UIGeometryIcon"/>.</returns>
    public Material CreateCustomSurfaceMaterial(string surfaceModule, string name = "")
    {
        Shader shader = AlcoGUIShaders.ComposeSurface(_shaders, surfaceModule, name);
        Material material = shader.CreateMaterial(string.IsNullOrEmpty(name) ? surfaceModule : name);
        material.Blend = PremultiplyBlend;
        material.Rasterization = new RasterizationState { Cull = CullMode.None };
        return material;
    }

    /// <summary>Compiles an in-memory <c>IUISurface</c> Slang source and returns a premultiplied <see cref="Material"/>.</summary>
    /// <param name="surfaceModule">Unique logical module name matching the <c>module</c> declaration in <paramref name="source"/>.</param>
    /// <param name="virtualPath">Virtual path for diagnostics.</param>
    /// <param name="source">Slang source importing <c>AlcoGUI_Surface</c>.</param>
    /// <param name="name">Optional diagnostic material name.</param>
    /// <returns>A newly created <see cref="Material"/> ready for per-node parameter customization.</returns>
    public Material CreateCustomSurfaceMaterialFromSource(string surfaceModule, string virtualPath, string source, string name = "")
    {
        Shader shader = AlcoGUIShaders.ComposeSurfaceFromSource(_shaders, surfaceModule, virtualPath, source, name);
        Material material = shader.CreateMaterial(string.IsNullOrEmpty(name) ? surfaceModule : name);
        material.Blend = PremultiplyBlend;
        material.Rasterization = new RasterizationState { Cull = CullMode.None };
        return material;
    }
}
