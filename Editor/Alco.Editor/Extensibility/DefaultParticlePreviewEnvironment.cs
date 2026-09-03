using Alco.Graphics;
using Alco.Particles;
using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The built-in <see cref="IParticlePreviewEnvironment"/>: default particle systems
/// (the 3D one with read-only depth against the preview's non-reversed projection)
/// and the <see cref="DefaultPreviewPipelineFactory"/>. Stateless; use
/// <see cref="Instance"/>.
/// </summary>
public sealed class DefaultParticlePreviewEnvironment : IParticlePreviewEnvironment
{
    private DefaultParticlePreviewEnvironment()
    {
    }

    /// <summary>The shared instance.</summary>
    public static DefaultParticlePreviewEnvironment Instance { get; } = new();

    /// <inheritdoc />
    public GpuParticleSystem2D CreateSystem2D(RenderingSystem rendering) => new(rendering);

    /// <inheritdoc />
    public GpuParticleSystem3D CreateSystem3D(RenderingSystem rendering) => new(rendering)
    {
        // The preview pipeline clears depth to 1 with a plain (non-reversed) projection.
        DepthStencilState = DepthStencilState.Read,
    };

    /// <inheritdoc />
    public IPreviewPipelineFactory CreatePipelineFactory(bool is3D) => DefaultPreviewPipelineFactory.Instance;
}
