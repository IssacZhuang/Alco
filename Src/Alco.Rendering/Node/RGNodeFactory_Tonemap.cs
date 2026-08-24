using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Factory for the tone-mapping chain-transform node: holds the node's
/// <see cref="RGNode_Tonemap.Descriptor"/> (shader references resolve through
/// the shared shader system at load time). The post <see cref="RenderChain"/>
/// and the chain's output layout come from the factory context's services.
/// </summary>
public class RGNodeFactory_Tonemap : RenderNodeFactory
{
    /// <summary>The node's construction data.</summary>
    public required RGNode_Tonemap.Descriptor Descriptor { get; set; }

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        RGNode_Tonemap.Descriptor descriptor = Descriptor;
        return new RGNode_Tonemap(
            context.Rendering,
            context.Graph,
            context.Services.Get<RenderChain>(),
            context.Services.Get<GPUAttachmentLayout>(),
            in descriptor);
    }
}
