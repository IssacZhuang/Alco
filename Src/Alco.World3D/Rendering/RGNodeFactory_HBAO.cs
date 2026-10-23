using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the HBAO+ render plugin node, holding its <see cref="RGNode_HBAO.Descriptor"/>; wiring into a deferred composition stays with the composing code through <see cref="RGNode_HBAO.Attach"/>.
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
