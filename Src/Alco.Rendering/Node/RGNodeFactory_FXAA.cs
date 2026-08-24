using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Factory for the FXAA chain-transform node: holds the node's
/// <see cref="RGNode_FXAA.Descriptor"/> (the shader reference resolves through
/// the shared shader system at load time; the quality axis is a generic value
/// specialization of the fxaa module, one specialized shader per preset). The
/// post <see cref="RenderChain"/> and the chain's output layout come from the
/// factory context's services.
/// </summary>
public class RGNodeFactory_FXAA : RenderNodeFactory
{
    /// <summary>The node's construction data.</summary>
    public required RGNode_FXAA.Descriptor Descriptor { get; set; }

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        RGNode_FXAA.Descriptor descriptor = Descriptor;
        return new RGNode_FXAA(
            context.Rendering,
            context.Graph,
            context.Services.Get<RenderChain>(),
            context.Services.Get<GPUAttachmentLayout>(),
            in descriptor);
    }
}
