namespace Alco.Rendering;

using System.Numerics;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct ConntectableTileConstant
{
    public Matrix4x4 Model;
    public Vector4 Color;
    public Rect UvRect;
    public int2 Offset;
}