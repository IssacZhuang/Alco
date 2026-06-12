namespace Alco.Rendering;

/// <summary>
/// Thrown when image encoding fails due to invalid input or encoding errors.
/// </summary>
public sealed class ImageEncodeException : Exception
{
    public ImageEncodeException(string message) : base(message) { }
    public ImageEncodeException(string message, Exception inner) : base(message, inner) { }
}
