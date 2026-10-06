
using System.Numerics;

namespace Alco.Graphics;

/// <summary>
/// One color-attachment clear: the target attachment index and the RGBA value to
/// clear it to.
/// </summary>
public struct ClearColorData
{
    /// <summary>
    /// Initializes the clear for one color attachment.
    /// </summary>
    public ClearColorData(uint index, Vector4 color)
    {
        Color = color;
        Index = index;
    }

    /// <summary>
    /// The RGBA value to clear to.
    /// </summary>
    public Vector4 Color;
    /// <summary>
    /// Which color attachment of the render pass the clear applies to.
    /// </summary>
    public uint Index;
}