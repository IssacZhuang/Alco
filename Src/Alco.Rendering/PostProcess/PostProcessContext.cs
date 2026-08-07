
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The per-stage input/output of a <see cref="PostProcessChain"/> execution.
/// </summary>
public sealed class PostProcessContext
{
    /// <summary>
    /// The owning chain. Use <see cref="PostProcessChain.Blit"/> for a plain copy when an
    /// effect needs to pass its input through unchanged.
    /// </summary>
    public PostProcessChain Chain { get; }

    /// <summary>
    /// The texture the stage reads from: the scene texture for the first enabled stage,
    /// otherwise the output of the previous enabled stage.
    /// </summary>
    public RenderTexture Source { get; }

    /// <summary>
    /// The frame buffer the stage writes into: a chain-owned intermediate texture, or the
    /// final destination (e.g. the swapchain) for the last enabled stage.
    /// </summary>
    public GPUFrameBuffer Destination { get; }

    /// <summary>
    /// The pipeline's scene texture — the chain's original input, including its depth
    /// attachment. Stages that need scene depth should sample this texture regardless of
    /// their position in the chain.
    /// </summary>
    public RenderTexture SceneSource { get; }

    /// <summary>
    /// Creates a context. Called by <see cref="PostProcessChain"/> while executing the chain.
    /// </summary>
    public PostProcessContext(PostProcessChain chain, RenderTexture source, GPUFrameBuffer destination, RenderTexture sceneSource)
    {
        Chain = chain;
        Source = source;
        Destination = destination;
        SceneSource = sceneSource;
    }
}
