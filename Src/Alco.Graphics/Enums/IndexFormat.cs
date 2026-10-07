namespace Alco.Graphics;

/// <summary>
/// The data type of the values in an index buffer.
/// </summary>
public enum IndexFormat
{
    /// <summary>No index format declared.</summary>
    Undefined = 0,
    /// <summary>16-bit unsigned indices.</summary>
    UInt16 = 1,
    /// <summary>32-bit unsigned indices.</summary>
    UInt32 = 2,
}