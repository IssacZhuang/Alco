namespace Alco.IO;

/// <summary>
/// Defines the services the asset system requires from its host.
/// </summary>
public interface IAssetSystemHost
{
    /// <summary>
    /// Raised when the host is disposed.
    /// </summary>
    event Action OnDispose;
    /// <summary>
    /// Queues an action for execution on the host's main thread.
    /// </summary>
    void PostToMainThread(Action action);
    /// <summary>
    /// Writes an informational message to the host log.
    /// </summary>
    void LogInfo(ReadOnlySpan<char> message);
    /// <summary>
    /// Writes a warning message to the host log.
    /// </summary>
    void LogWarning(ReadOnlySpan<char> message);
    /// <summary>
    /// Writes an error message to the host log.
    /// </summary>
    void LogError(ReadOnlySpan<char> message);
    /// <summary>
    /// Writes a success message to the host log.
    /// </summary>
    void LogSuccess(ReadOnlySpan<char> message);
}
