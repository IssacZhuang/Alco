using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// A vertex structure that contains position, normal, texture coordinate and tangent data.
/// The tangent's w component is the bitangent sign: bitangent = w * cross(normal, tangent).
/// </summary>
public unsafe struct VertexPositionNormalTextureTangent
{
    /// <summary>
    /// The size of the vertex structure in bytes.
    /// </summary>
    public static readonly int SizeInBytes = sizeof(VertexPositionNormalTextureTangent);

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
    /// The tangent vector of the vertex; w flips the bitangent for mirrored UVs.
    /// </summary>
    public Vector4 Tangent;

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexPositionNormalTextureTangent"/> struct.
    /// </summary>
    /// <param name="position">The position of the vertex.</param>
    /// <param name="normal">The normal vector of the vertex.</param>
    /// <param name="uv">The texture coordinate of the vertex.</param>
    /// <param name="tangent">The tangent of the vertex (w is the bitangent sign).</param>
    public VertexPositionNormalTextureTangent(Vector3 position, Vector3 normal, Vector2 uv, Vector4 tangent)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = tangent;
    }
}
