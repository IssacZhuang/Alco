using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Factory for the tone-mapping chain-transform node: holds the plain-copy
/// shader used by the <see cref="TonemapType.Linear"/> operator and the six
/// operator shaders (all resolved at load time). The post
/// <see cref="RenderChain"/> and the chain's output layout come from the factory
/// context's services.
/// </summary>
public class RGNodeFactory_Tonemap : RenderNodeFactory
{
    /// <summary>The plain-copy shader used by the Linear operator.</summary>
    public required Shader BlitShader { get; set; }
    /// <summary>The Reinhard operator shader.</summary>
    public required Shader ReinhardShader { get; set; }
    /// <summary>The Uncharted 2 operator shader.</summary>
    public required Shader Uncharted2Shader { get; set; }
    /// <summary>The filmic operator shader.</summary>
    public required Shader FilmicShader { get; set; }
    /// <summary>The ACES operator shader.</summary>
    public required Shader AcesShader { get; set; }
    /// <summary>The neutral operator shader.</summary>
    public required Shader NeutralShader { get; set; }
    /// <summary>The AgX operator shader.</summary>
    public required Shader AgxShader { get; set; }

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        return new RGNode_Tonemap(
            context.Rendering,
            context.Graph,
            context.Services.Get<RenderChain>(),
            context.Services.Get<GPUAttachmentLayout>(),
            BlitShader,
            ReinhardShader,
            Uncharted2Shader,
            FilmicShader,
            AcesShader,
            NeutralShader,
            AgxShader);
    }
}
