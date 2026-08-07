
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A single stage of a <see cref="PostProcessChain"/> (bloom, color grading, tone mapping,
/// FXAA, ...). Stages execute in ascending <see cref="Order"/>; each stage reads its input
/// from <see cref="PostProcessContext.Source"/> and writes the result into
/// <see cref="PostProcessContext.Destination"/>. The chain owns the intermediate
/// ping-pong textures — a stage only implements the effect itself.
/// </summary>
public abstract class PostProcessStage : AutoDisposable
{
    /// <summary>
    /// Whether the stage participates in the chain. Disabled stages are skipped entirely.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The execution order inside the chain. Lower values run earlier.
    /// </summary>
    public abstract int Order { get; }

    /// <summary>
    /// Applies the effect for this frame.
    /// </summary>
    /// <param name="context">The per-frame input/output of the chain execution.</param>
    public abstract void Apply(PostProcessContext context);

    /// <summary>
    /// Recreates resolution-dependent resources after the pipeline's scene texture was resized.
    /// </summary>
    public virtual void Resize(uint width, uint height)
    {
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
    }
}
