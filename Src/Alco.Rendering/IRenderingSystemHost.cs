namespace Alco.Rendering;

/// <summary>
/// The host callbacks consumed by the rendering system.
/// </summary>
public interface IRenderingSystemHost
{
    /// <summary>
    /// Raised once per update with the elapsed time in seconds.
    /// </summary>
    event Action<float> OnUpdate;

    /// <summary>
    /// Raised when the rendering system is disposed.
    /// </summary>
    event Action OnDispose;
}

