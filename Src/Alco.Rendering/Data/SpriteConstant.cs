namespace Alco.Rendering;

using System.Numerics;
using System.Runtime.InteropServices;

/// <summary>
/// Per-draw constants for sprite rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SpriteConstant
{
    public Matrix4x4 Model;
    public Vector4 Color;
    public Rect UvRect;
}