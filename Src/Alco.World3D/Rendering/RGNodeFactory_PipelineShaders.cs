using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The shader set the deferred PBR composition itself consumes: the final blit,
/// the deferred lighting pass and the optional volumetric light overlay (all
/// resolved at load time). This is configuration data for
/// <see cref="RenderPipelines.CreatePBRDeferred"/>, not a node factory proper —
/// it shares the factory asset channel so a pipeline's every shader binding
/// lives in .rnfact data.
/// </summary>
public class RGNodeFactory_PipelineShaders : RenderNodeFactory
{
    /// <summary>The plain-copy shader the final blit uses.</summary>
    public required Shader BlitShader { get; set; }
    /// <summary>The deferred lighting shader.</summary>
    public required Shader LightingShader { get; set; }
    /// <summary>The volumetric light (god rays) overlay shader, or null to skip
    /// the overlay entirely.</summary>
    public Shader? VolumetricLightShader { get; set; }

    /// <summary>This factory configures the composition, not a node.</summary>
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        throw new NotSupportedException(
            "PipelineShaders is composition configuration data; pass its shaders to " +
            "RenderPipelines.CreatePBRDeferred instead of calling Create.");
    }
}
