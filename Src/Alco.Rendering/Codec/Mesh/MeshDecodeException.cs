namespace Alco.Rendering;

/// <summary>
/// Thrown when mesh decoding fails due to corrupt data, unsupported features, or invalid format.
/// </summary>
public sealed class MeshDecodeException : Exception
{
    /// <summary>
    /// Initializes a new instance with the specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public MeshDecodeException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance with the specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The exception that caused this error.</param>
    public MeshDecodeException(string message, Exception inner) : base(message, inner) { }
}
