using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Factory for the FXAA chain-transform node: holds the node's
/// <see cref="RGNode_FXAA.Descriptor"/> (the shader reference resolves through
/// the shared shader system at load time; the quality and mode axes are
/// generic value specializations of the fxaa module, one specialized shader
/// per (preset, mode) pair). The post <see cref="RenderChain"/> and the
/// chain's output layout come from the factory context's services; the
/// optional <see cref="SceneDepthSource"/> service enables the descriptor's
/// depth-aware modes.
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
