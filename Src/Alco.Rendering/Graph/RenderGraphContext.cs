using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The per-frame execution context passed to <see cref="IRenderGraphNode.Execute"/>.
/// Exposes the frame's destination and timing information. Nodes resolve their
/// transient resources through the <see cref="RenderGraphTexture.Texture"/> facades
/// they captured at registration — the facades are guaranteed to be backed by real
/// GPU textures for the duration of the node's Execute call.
/// <br/>A single reused instance is passed to every node; it is only valid during the
/// Execute call — do not store it.
/// </summary>
public sealed class RenderGraphContext
{
    private GPUFrameBuffer? _destination;
    private float _deltaTime;

    internal RenderGraphContext(RenderingSystem rendering, RenderProfiler? profiler)
    {
        Rendering = rendering;
        Profiler = profiler;
    }

    /// <summary>The rendering system, for creating GPU resources.</summary>
    public RenderingSystem Rendering { get; }

    /// <summary>The profiler exposed to nodes through the execution context, or null.</summary>
    public RenderProfiler? Profiler { get; internal set; }

    /// <summary>
    /// The frame's final output frame buffer (e.g. the swapchain), or null for a
    /// minimized/headless view.
    /// </summary>
    public GPUFrameBuffer? Destination
    {
        get => _destination;
    }

    /// <summary>The frame delta time in seconds.</summary>
    public float DeltaTime
    {
        get => _deltaTime;
    }

    /// <summary>Resets the per-frame fields (called by the graph before each frame).</summary>
    internal void Reset(GPUFrameBuffer? destination, float deltaTime)
    {
        _destination = destination;
        _deltaTime = deltaTime;
    }
}
