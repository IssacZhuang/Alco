using System.Numerics;
using System.Runtime.InteropServices;

namespace Vocore.Engine;

/// <summary>
/// The text data for GPU text rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TextData
{
    public Vector4 UVRect;
    public Vector4 Color;
    public Vector4 Offset; //the z and w components are unused but for memory alignment
}