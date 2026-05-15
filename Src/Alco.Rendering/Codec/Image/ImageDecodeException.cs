namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Thrown when image decoding fails due to corrupt data, unsupported features, or invalid headers.
/// </summary>
public sealed class ImageDecodeException : Exception
{
    public ImageDecodeException(string message) : base(message) { }
    public ImageDecodeException(string message, Exception inner) : base(message, inner) { }
}
