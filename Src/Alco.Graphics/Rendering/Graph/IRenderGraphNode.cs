// Render graph node contracts and linear content-chain threading state.

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>
/// A unit of rendering registered in a <see cref="RenderGraph"/> that declares one or more passes in <see cref="Setup"/>
/// and records GPU commands for surviving passes in <see cref="Execute"/>.
/// </summary>
public interface IRenderGraphNode
{
    /// <summary>Whether this node participates in the current frame; disabled nodes skip setup and execution.</summary>
    bool IsEnabled => true;

    /// <summary>
    /// Declares this frame's passes and resource accesses before compilation and execution.
    /// Runs on the per-frame hot path and should avoid managed allocations.
    /// </summary>
    /// <param name="builder">The frame's graph builder, valid only during this call.</param>
    void Setup(RenderGraphBuilder builder);

    /// <summary>Records GPU commands for one surviving pass declared by this node.</summary>
    /// <param name="context">The execution context for resolving handles and recording commands.</param>
    /// <param name="passId">The node-local pass identifier passed to <see cref="RenderGraphBuilder.AddPass"/>.</param>
    void Execute(in RenderGraphContext context, int passId);

    /// <summary>Notifies the node when the graph's viewport extent changes.</summary>
    /// <param name="extent">The new viewport extent in pixels.</param>
    void Resize(Extent2D extent) { }
}

/// <summary>Convenience base class for single-pass render graph nodes.</summary>
public abstract class RenderGraphNode : IRenderGraphNode
{
    /// <summary>Creates a single-pass node with the given diagnostic name.</summary>
    /// <param name="name">The pass name shown in profiling and debug groups.</param>
    protected RenderGraphNode(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>The diagnostic pass name.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public virtual bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        SetupPass(builder, builder.AddPass(Name, this));
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context, int passId)
    {
        ExecutePass(in context);
    }

    /// <inheritdoc />
    public virtual void Resize(Extent2D extent) { }

    /// <summary>Declares this pass's resource reads, writes, and optional output status.</summary>
    /// <param name="builder">The frame builder for creating or inspecting resources.</param>
    /// <param name="pass">The pass builder for declaring resource accesses.</param>
    protected abstract void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass);

    /// <summary>Records GPU commands for this pass when it survives culling.</summary>
    /// <param name="context">The execution context.</param>
    protected abstract void ExecutePass(in RenderGraphContext context);
}

/// <summary>
/// Tracks the current texture handle through a linear sequence of post-process or overlay nodes during graph setup.
/// Reset to the scene color root before each frame; transform nodes read <see cref="Current"/> and call <see cref="Advance"/>.
/// </summary>
public sealed class RenderChain
{
    /// <summary>The texture currently holding the accumulated content at this point in setup.</summary>
    public RGTexture Current { get; private set; }

    /// <summary>Resets the chain to the frame's initial scene color texture.</summary>
    /// <param name="root">A valid texture handle serving as the chain root.</param>
    public void Reset(RGTexture root)
    {
        if (!root.IsValid)
            throw new ArgumentException("The render chain root must be a valid texture handle.", nameof(root));
        Current = root;
    }

    /// <summary>Advances the chain to a transform node's output texture.</summary>
    /// <param name="next">A valid texture handle holding the transformed content.</param>
    public void Advance(RGTexture next)
    {
        if (!next.IsValid)
            throw new ArgumentException("The next render chain texture must be a valid handle.", nameof(next));
        Current = next;
    }
}
