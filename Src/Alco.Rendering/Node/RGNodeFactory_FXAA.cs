using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Factory for the FXAA chain-transform node: holds the scene-copy shader
/// (resolved at load time), the quality preset and the edge-detection threshold.
/// The quality axis resolves inside the effect as a generic value specialization
/// of the fxaa module (<c>MainPS&lt;let Quality : int&gt;</c>), one specialized
/// shader per preset. The post <see cref="RenderChain"/> and the chain's output
/// layout come from the factory context's services.
/// </summary>
public class RGNodeFactory_FXAA : RenderNodeFactory
{
    /// <summary>The scene-copy shader used for the final blit.</summary>
    public required Shader SceneCopyShader { get; set; }

    /// <summary>The quality preset; changing it selects a different specialized shader.</summary>
    public FXAAQuality Quality { get; set; } = FXAAQuality.Medium;

    /// <summary>The edge detection threshold (0.063 - 0.333).</summary>
    public float Threshold { get; set; } = 0.125f;

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        return new RGNode_FXAA(
            context.Graph,
            context.Services.Get<RenderChain>(),
            context.Services.Get<GPUAttachmentLayout>(),
            context.Rendering.CreateFXAA(SceneCopyShader))
        {
            Quality = Quality,
            Threshold = Threshold,
        };
    }
}
