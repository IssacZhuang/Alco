using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the voxel global illumination render plugin node: holds the nine
/// shaders of <see cref="VoxelGiShaders"/> (resolved at load time) and the
/// clipmap / trace parameters. The material compiler composing the per-surface
/// voxelize feed comes from the factory context's services; graph resources and
/// the lighting hookup stay with the composing code through
/// <see cref="RGNode_VoxelGI.Attach"/>. The triangle voxelization shader is not
/// mapped here — it composes per material surface through the compiler.
/// </summary>
public class RGNodeFactory_VoxelGI : RenderNodeFactory
{
    /// <summary>The voxel clear shader.</summary>
    public required Shader ClearShader { get; set; }
    /// <summary>The direct light injection shader.</summary>
    public required Shader InjectShader { get; set; }
    /// <summary>The radiance mip downsample shader.</summary>
    public required Shader MipShader { get; set; }
    /// <summary>The cascading mip chain shader.</summary>
    public required Shader MipChainShader { get; set; }
    /// <summary>The multi-bounce propagation shader.</summary>
    public required Shader PropagateShader { get; set; }
    /// <summary>The cone tracing shader.</summary>
    public required Shader TraceShader { get; set; }
    /// <summary>The temporal demosaic shader.</summary>
    public required Shader DemosaicShader { get; set; }
    /// <summary>The blue-noise tile bake shader.</summary>
    public required Shader BlueNoiseShader { get; set; }
    /// <summary>The full-resolution upsample shader, or null when not used as a plugin.</summary>
    public Shader? UpsampleShader { get; set; }

    /// <summary>The voxel resolution of each clipmap level (power of two, ≥ 16).</summary>
    public int Resolution { get; set; } = 128;
    /// <summary>The voxel size of the finest clipmap level in world units.</summary>
    public float BaseVoxelSize { get; set; } = 0.25f;
    /// <summary>The cone-trace resolution relative to the G-buffer (0.25 - 1.0).</summary>
    public float TraceResolutionScale { get; set; } = 0.5f;

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        return new RGNode_VoxelGI(
            context.Rendering,
            context.Services.Get<MaterialCompiler>(),
            new VoxelGiShaders
            {
                Clear = ClearShader,
                Inject = InjectShader,
                Mip = MipShader,
                MipChain = MipChainShader,
                Propagate = PropagateShader,
                Trace = TraceShader,
                Demosaic = DemosaicShader,
                BlueNoise = BlueNoiseShader,
                Upsample = UpsampleShader,
            },
            width: context.Graph.Width,
            height: context.Graph.Height,
            resolution: Resolution,
            baseVoxelSize: BaseVoxelSize,
            traceResolutionScale: TraceResolutionScale);
    }
}
