using System;
using System.Numerics;
using Alco;

namespace Alco.Rendering;

/// <summary>
/// An interleaved vertex with a 3D position and texture coordinates.
/// </summary>
public unsafe struct Vertex
{
    /// <summary>
    /// The size of a <see cref="Vertex"/> in bytes.
    /// </summary>
    public static readonly int SizeInBytes = sizeof(Vertex);

    public Vector3 position;
    public Vector2 uv;

    /// <summary>
    /// Initializes a new instance of the <see cref="Vertex"/> struct.
    /// </summary>
    /// <param name="position">The vertex position.</param>
    /// <param name="uv">The vertex texture coordinates.</param>
    public Vertex(Vector3 position, Vector2 uv)
    {
        this.position = position;
        this.uv = uv;
    }

}


