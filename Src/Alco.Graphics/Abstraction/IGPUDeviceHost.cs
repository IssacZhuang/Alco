namespace Alco.Graphics;

/// <summary>
/// Host lifecycle and logging surface a GPU device reports back to. The host (typically the engine)
/// owns the frame loop and shutdown, and the device subscribes to its events to run per-frame work
/// and to release itself at teardown.
/// </summary>
public interface IGPUDeviceHost
{
    /// <summary>Raised by the host at the end of each frame, after frame work is done and before presentation; the device uses it for per-frame processing such as deferred resource disposal.</summary>
    event Action OnEndFrame;

    /// <summary>Raised by the host when it is being disposed (engine shutdown); the device disposes itself in response.</summary>
    event Action OnDispose;

    /// <summary>Logs an informational message to the host's log.</summary>
    /// <param name="message">The message to log.</param>
    void LogInfo(ReadOnlySpan<char> message);

    /// <summary>Logs a warning message to the host's log.</summary>
    /// <param name="message">The message to log.</param>
    void LogWarning(ReadOnlySpan<char> message);

    /// <summary>Logs an error message to the host's log.</summary>
    /// <param name="message">The message to log.</param>
    void LogError(ReadOnlySpan<char> message);

    /// <summary>Logs a success message to the host's log.</summary>
    /// <param name="message">The message to log.</param>
    void LogSuccess(ReadOnlySpan<char> message);
}