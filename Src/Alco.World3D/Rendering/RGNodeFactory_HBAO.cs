using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the HBAO+ render plugin node: holds the raw AO shader and the
/// bilateral blur shader (resolved at load time). The node needs no
/// pipeline-shape inputs at construction — wiring into a deferred composition
/// (lighting node, G-buffer, scene environment) stays with the composing code
/// through <see cref="RGNode_HBAO.Attach"/>.
/// </summary>
public class RGNodeFactory_HBAO : RenderNodeFactory
{
    /// <summary>The raw AO shader.</summary>
    public required Shader HbaoShader { get; set; }
    /// <summary>The bilateral blur shader.</summary>
    public required Shader BlurShader { get; set; }

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        return new RGNode_HBAO(context.Rendering, HbaoShader, BlurShader);
    }
}
