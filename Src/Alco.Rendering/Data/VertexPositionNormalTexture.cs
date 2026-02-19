using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// A vertex structure that contains position, normal, and texture coordinate data.
/// </summary>
public unsafe struct VertexPositionNormalTexture
{
    /// <summary>
    /// The size of the vertex structure in bytes.
    /// </summary>
    public static readonly int SizeInBytes = sizeof(VertexPositionNormalTexture);

    /// <summary>
    /// The position of the vertex in 3D space.
    /// </summary>
    public Vector3 Position;

    /// <summary>
    /// The normal vector of the vertex.
    /// </summary>
    public Vector3 Normal;

    /// <summary>
    /// The texture coordinate of the vertex.
    /// </summary>
    public Vector2 UV;

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexPositionNormalTexture"/> struct.
    /// </summary>
    /// <param name="position">The position of the vertex.</param>
    /// <param name="normal">The normal vector of the vertex.</param>
    /// <param name="uv">The texture coordinate of the vertex.</param>
    public VertexPositionNormalTexture(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
    }
}
