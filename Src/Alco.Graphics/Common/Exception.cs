namespace Alco.Graphics;

/// <summary>
/// Thrown by the graphics layer for invalid API usage or backend failures.
/// </summary>
public class GraphicsException : Exception
{
    /// <summary>
    /// Initializes the exception with a message describing the failure.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public GraphicsException(string message) : base(message)
    {
    }

    /// <summary>
    /// Throws if the given GPU object is already disposed.
    /// </summary>
    /// <param name="obj">The GPU object to check.</param>
    /// <exception cref="GraphicsException">The object is disposed.</exception>
    public static void ThrowIfDisposed(BaseGPUObject obj)
    {
        if (obj.IsDisposed)
        {
            throw new GraphicsException($"Object {obj.Name} is disposed.");
        }
    }
}

