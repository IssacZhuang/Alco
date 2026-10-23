namespace Alco.Rendering;

using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>
/// Per-draw constants for rendering a connectable tile sprite.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ConntectableTileConstant
{
    public Matrix4x4 Model;
    public Vector4 Color;
    public Rect UvRect;
    public Vector2 Size;
    public Vector2 Offset;
}