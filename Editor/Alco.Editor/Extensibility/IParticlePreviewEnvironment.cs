using Alco.Particles;
using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The environment behind the particle effect preview: creates the GPU particle
/// systems (with their render module and depth state) and the preview pipeline
/// factory for the effect's dimension. Registered as a service; replace it to host
/// the preview on a game-specific particle or pipeline setup.
/// </summary>
public interface IParticlePreviewEnvironment
{
    /// <summary>Creates the 2D particle system the preview simulates and renders with.</summary>
    /// <param name="rendering">The rendering system.</param>
    GpuParticleSystem2D CreateSystem2D(RenderingSystem rendering);

    /// <summary>Creates the 3D particle system the preview simulates and renders with.</summary>
    /// <param name="rendering">The rendering system.</param>
    GpuParticleSystem3D CreateSystem3D(RenderingSystem rendering);

    /// <summary>Creates the preview pipeline factory for the given effect dimension.</summary>
    /// <param name="is3D">True for a 3D effect preview, false for 2D.</param>
    IPreviewPipelineFactory CreatePipelineFactory(bool is3D);
}
