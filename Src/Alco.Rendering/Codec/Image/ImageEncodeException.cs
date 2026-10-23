namespace Alco.Rendering;

/// <summary>
/// Thrown when image encoding fails due to invalid input or encoding errors.
/// </summary>
public sealed class ImageEncodeException : Exception
{
    /// <summary>
    /// Initializes a new instance with the specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ImageEncodeException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance with the specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The exception that caused this error.</param>
    public ImageEncodeException(string message, Exception inner) : base(message, inner) { }
}
