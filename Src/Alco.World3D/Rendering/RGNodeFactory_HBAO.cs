using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the HBAO+ render plugin node: holds the node's
/// <see cref="RGNode_HBAO.Descriptor"/> (shader references resolve through the
/// shared shader system at load time). The node needs no pipeline-shape inputs
/// at construction — wiring into a deferred composition (lighting node,
/// G-buffer, scene environment) stays with the composing code through
/// <see cref="RGNode_HBAO.Attach"/>.
/// </summary>
public class RGNodeFactory_HBAO : RenderNodeFactory
{
    /// <summary>The node's construction data.</summary>
    public required RGNode_HBAO.Descriptor Descriptor { get; set; }

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        RGNode_HBAO.Descriptor descriptor = Descriptor;
        return new RGNode_HBAO(context.Rendering, in descriptor);
    }
}
