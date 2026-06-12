namespace Alco.Graphics;

/// <summary>
/// Tracks the state of a texture readback request.
/// </summary>
public enum GPUTextureReadbackStatus
{
    /// <summary>
    /// The request is ready to be used.
    /// </summary>
    Idle,

    /// <summary>
    /// The request is waiting for GPU readback completion.
    /// </summary>
    Pending,

    /// <summary>
    /// The request completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The request completed with an error.
    /// </summary>
    Failed,
}

/// <summary>
/// Reusable state object for an asynchronous GPU texture readback.
/// </summary>
public sealed class GPUTextureReadbackRequest
{
    private GPUTextureReadbackStatus _status;
    private Exception? _error;

    /// <summary>
    /// Gets the current request status.
    /// </summary>
    public GPUTextureReadbackStatus Status => _status;

    /// <summary>
    /// Gets a value indicating whether the request is waiting for GPU completion.
    /// </summary>
    public bool IsPending => _status == GPUTextureReadbackStatus.Pending;

    /// <summary>
    /// Gets a value indicating whether the request has completed successfully or failed.
    /// </summary>
    public bool IsCompleted => _status == GPUTextureReadbackStatus.Completed || _status == GPUTextureReadbackStatus.Failed;

    /// <summary>
    /// Gets the error recorded for a failed request, or null when the request has not failed.
    /// </summary>
    public Exception? Error => _error;

    /// <summary>
    /// Resets a completed request so it can be used again.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the request is still pending.</exception>
    public void Reset()
    {
        if (_status == GPUTextureReadbackStatus.Pending)
        {
            throw new InvalidOperationException("Cannot reset a pending texture readback request.");
        }

        _error = null;
        _status = GPUTextureReadbackStatus.Idle;
    }

    /// <summary>
    /// Throws the recorded readback failure, if any.
    /// </summary>
    public void ThrowIfFailed()
    {
        if (_error != null)
        {
            throw _error;
        }
    }

    internal void Begin()
    {
        if (_status == GPUTextureReadbackStatus.Pending)
        {
            throw new InvalidOperationException("Texture readback request is already pending.");
        }

        _error = null;
        _status = GPUTextureReadbackStatus.Pending;
    }

    internal void CancelBegin()
    {
        _error = null;
        _status = GPUTextureReadbackStatus.Idle;
    }

    internal void Complete()
    {
        _error = null;
        _status = GPUTextureReadbackStatus.Completed;
    }

    internal void Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _error = error;
        _status = GPUTextureReadbackStatus.Failed;
    }
}
