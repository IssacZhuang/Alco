namespace Alco.Rendering;

/// <summary>
/// Thrown when mesh decoding fails due to corrupt data, unsupported features, or invalid format.
/// </summary>
public sealed class MeshDecodeException : Exception
{
    public MeshDecodeException(string message) : base(message) { }
    public MeshDecodeException(string message, Exception inner) : base(message, inner) { }
}
