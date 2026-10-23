using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Constants for the gaussian blur compute pass.
/// </summary>
public struct GaussianBlurConstant
{
    public int2 texSize;
    public int2 kernelSize;
    public int2 rectOrigin;
    public int2 rectSize;
    public float kernelSum;
}
