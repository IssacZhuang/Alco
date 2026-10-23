namespace Alco.Rendering;

/// <summary>
/// Frame-level recording scope returned by <see cref="RenderContext.BeginFrame"/> that batches
/// all passes into one command buffer submitted when the scope is disposed. The instance is
/// recycled per frame, and all APIs are not thread safe.
/// </summary>
public sealed class RenderFrameScope : IDisposable
{
    private readonly RenderContext _owner;
    private bool _closed;

    internal RenderFrameScope(RenderContext owner)
    {
        _owner = owner;
    }

    /// <summary>Reopens the scope for a new frame. Driven by <see cref="RenderContext.BeginFrame"/>.</summary>
    internal void Activate()
    {
        _closed = false;
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
