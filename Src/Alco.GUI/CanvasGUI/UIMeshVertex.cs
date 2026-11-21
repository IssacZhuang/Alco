using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.GUI;

/// <summary>32-byte GPU vertex record used by <see cref="Canvas.DrawMesh"/>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct UIMeshVertex
{
    /// <summary>Vertex position in canvas world space.</summary>
    public Vector2 Position;

    /// <summary>Texture UV coordinate.</summary>
    public Vector2 Uv;

    /// <summary>Vertex color in straight RGBA.</summary>
    public Vector4 Color;
}
