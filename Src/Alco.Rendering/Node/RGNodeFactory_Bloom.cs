using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Factory for the bloom chain-transform node: holds the node's
/// <see cref="RGNode_Bloom.Descriptor"/> (shader references resolve through the
/// shared shader system at load time). The post <see cref="RenderChain"/> and
/// the chain's output layout come from the factory context's services.
/// </summary>
public class RGNodeFactory_Bloom : RenderNodeFactory
{
    /// <summary>The node's construction data.</summary>
    public required RGNode_Bloom.Descriptor Descriptor { get; set; }

    /// <summary>Whether the created node is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        RGNode_Bloom.Descriptor descriptor = Descriptor;
        return new RGNode_Bloom(
            context.Rendering,
            context.Graph,
            context.Services.Get<RenderChain>(),
            context.Services.Get<GPUAttachmentLayout>(),
            in descriptor)
        {
            IsEnabled = Enabled,
        };
    }
}
