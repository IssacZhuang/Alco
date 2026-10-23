using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Factory for the FXAA chain-transform node, holding its descriptor and resolving the chain,
/// output layout and optional depth source from the factory context services.
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
            context.Services.TryGet<SceneDepthSource>()?.Resource,
            in descriptor);
    }
}
