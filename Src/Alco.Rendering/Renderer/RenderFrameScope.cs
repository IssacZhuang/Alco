namespace Alco.Rendering;

/// <summary>
/// Frame-level recording scope returned by <see cref="RenderContext.BeginFrame"/>:
/// opens the context's command buffer on creation, so every pass (and any compute
/// recorded through <see cref="RenderContext.CommandBuffer"/>) inside the scope
/// lands in one shared buffer, submitted exactly once when the scope is disposed —
/// the render graph's one-submission-per-frame model, available to standalone code.
/// Consume with <c>using</c>, outermost of any pass scope:
/// <code>
/// using (RenderFrameScope frame = context.BeginFrame())
/// {
///     using (RenderPassScope pass1 = context.BeginPass(a)) { ... }  // no submit
///     using (RenderPassScope pass2 = context.BeginPass(b)) { ... }  // no submit
/// }   // single submission here
/// </code>
/// Disposing with a pass still open discards the whole buffer without submitting
/// (the same recovery as the render graph's abort path); disposing twice throws.
/// All APIs are not thread safe, same as the owning <see cref="RenderContext"/>.
/// </summary>
public sealed class RenderFrameScope : IDisposable
{
    private readonly RenderContext _owner;
    private bool _closed;

    internal RenderFrameScope(RenderContext owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Ends the frame: submits the shared buffer once, or discards it without
    /// submitting when a pass scope is still open.
    /// </summary>
    public void Dispose()
    {
        if (_closed)
        {
            throw new InvalidOperationException("The frame scope has already been disposed.");
        }

        _closed = true;
        _owner.CloseFrame();
    }
}
