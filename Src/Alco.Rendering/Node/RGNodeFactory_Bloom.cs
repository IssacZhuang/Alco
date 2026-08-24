using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Factory for the bloom chain-transform node: holds the bloom effect's four
/// shaders and the chain node's scene-copy shader (resolved at load time),
/// plus the tunable threshold / intensity / spread / gamma parameters. The post
/// <see cref="RenderChain"/> and the chain's output layout come from the factory
/// context's services.
/// </summary>
public class RGNodeFactory_Bloom : RenderNodeFactory
{
    /// <summary>The bloom pyramid's plain-copy shader.</summary>
    public required Shader BlitShader { get; set; }
    /// <summary>The threshold pre-pass shader.</summary>
    public required Shader ClampShader { get; set; }
    /// <summary>The pyramid downsample shader.</summary>
    public required Shader DownsampleShader { get; set; }
    /// <summary>The pyramid upsample shader.</summary>
    public required Shader UpsampleShader { get; set; }
    /// <summary>The chain node's scene-copy shader.</summary>
    public required Shader SceneCopyShader { get; set; }

    /// <summary>Only pixels above this brightness contribute to the bloom effect.</summary>
    public float Threshold { get; set; } = 1f;
    /// <summary>The final output strength of the bloom effect.</summary>
    public float Intensity { get; set; } = 0.35f;
    /// <summary>How far the bloom spreads across the pyramid.</summary>
    public float Spread { get; set; } = 1f;
    /// <summary>The gamma correction value for bloom blending.</summary>
    public float Gamma { get; set; } = 2.2f;
    /// <summary>Whether the created node is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        return new RGNode_Bloom(
            context.Rendering,
            context.Graph,
            context.Services.Get<RenderChain>(),
            context.Services.Get<GPUAttachmentLayout>(),
            new Bloom(
                context.Rendering,
                BlitShader,
                ClampShader,
                DownsampleShader,
                UpsampleShader,
                targetDownSampleHeight: 11),
            SceneCopyShader)
        {
            Threshold = Threshold,
            Intensity = Intensity,
            Spread = Spread,
            Gamma = Gamma,
            IsEnabled = Enabled,
        };
    }
}
