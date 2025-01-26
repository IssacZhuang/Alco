using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

/// <summary>
/// The text data for GPU text rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TextData
{
    public Vector4 UVRect;
    public Vector4 Color;
    public Vector2 Offset;
    public Vector2 Size;
}