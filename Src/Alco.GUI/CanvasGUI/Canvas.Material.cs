using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public partial class Canvas
{
    private readonly ConditionalWeakTable<MaterialAsset, Dictionary<(bool Text, BlendState Blend), WeakReference<GraphicsMaterial>>> _materialCache = new();
    private readonly ShaderLibrary _spriteTemplate;
    private readonly ShaderLibrary _textTemplate;
    private readonly ShaderLibrary _defaultSurface;

    /// <summary>
    /// Gets the shared, weakly cached base material for an image surface with the given blend state.
    /// Use <see cref="CreateUIMaterialInstance"/> for node-specific bindings.
    /// </summary>
    /// <param name="asset">The surface, textures, and default parameters to compile.</param>
    /// <param name="blend">The blend state matching the surface's output.</param>
    /// <returns>The cached or newly compiled base material.</returns>
    public GraphicsMaterial GetSpriteMaterial(MaterialAsset asset, BlendState blend)
    {
        return GetMaterial(asset, false, blend);
    }

    /// <summary>
    /// Gets the shared base material for an IUISurface drawn through the glyph pipeline.
    /// Glyph coverage and premultiplied-alpha output are supplied by the text pass.
    /// </summary>
    /// <param name="asset">The surface, textures, and default parameters to compile.</param>
    /// <returns>The weakly cached or newly compiled text material.</returns>
    public GraphicsMaterial GetTextMaterial(MaterialAsset asset)
    {
        return GetMaterial(asset, true, BlendState.PremultipliedAlpha);
    }

    private GraphicsMaterial GetMaterial(MaterialAsset asset, bool text, BlendState blend)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        Dictionary<(bool Text, BlendState Blend), WeakReference<GraphicsMaterial>> variants = _materialCache.GetOrCreateValue(asset);
        (bool Text, BlendState Blend) key = (text, blend);
        if (variants.TryGetValue(key, out WeakReference<GraphicsMaterial>? reference)
            && reference.TryGetTarget(out GraphicsMaterial? cached) && !cached.IsDisposed)
        {
            return cached;
        }

        ShaderLibrary template = text ? _textTemplate : _spriteTemplate;
        GraphicsMaterial material = _renderingSystem.MaterialCompiler.Compile(asset, template, null, (source, shader) =>
        {
            GraphicsMaterial compiled = _renderingSystem.CreateGraphicsMaterial(shader, $"{source.Name}_{template.Name}");
            compiled.DepthStencilState = DepthStencilState.Read;
            compiled.BlendState = blend;
            return compiled;
        }, _defaultSurface);
        variants[key] = new WeakReference<GraphicsMaterial>(material);
        return material;
    }
}
