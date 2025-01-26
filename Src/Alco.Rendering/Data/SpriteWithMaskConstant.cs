namespace Alco.Rendering;

using System.Numerics;
using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential)]
public struct SpriteWithMaskConstant
{
    public Matrix4x4 Model;
    public BoundingBox2D Mask;
    public Vector4 Color;
    public Rect UvRect;
}