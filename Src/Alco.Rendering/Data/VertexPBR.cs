using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// The standard PBR vertex structure: position, normal, texture coordinate and tangent.
/// The tangent's w component is the bitangent sign: bitangent = w * cross(normal, tangent).
/// </summary>
public unsafe struct VertexPBR
{
    /// <summary>
    /// The size of the vertex structure in bytes.
    /// </summary>
    public static readonly int SizeInBytes = sizeof(VertexPBR);

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
    /// Initializes a new instance of the <see cref="VertexPBR"/> struct with an explicit tangent.
    /// </summary>
    /// <param name="position">The position of the vertex.</param>
    /// <param name="normal">The normal vector of the vertex.</param>
    /// <param name="uv">The texture coordinate of the vertex.</param>
    /// <param name="tangent">The tangent of the vertex (w is the bitangent sign).</param>
    public VertexPBR(Vector3 position, Vector3 normal, Vector2 uv, Vector4 tangent)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = tangent;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VertexPBR"/> struct with the default
    /// tangent (1, 0, 0, 1). Use this for procedurally generated geometry that does not
    /// require tangent-space normal mapping.
    /// </summary>
    /// <param name="position">The position of the vertex.</param>
    /// <param name="normal">The normal vector of the vertex.</param>
    /// <param name="uv">The texture coordinate of the vertex.</param>
    public VertexPBR(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Position = position;
        Normal = normal;
        UV = uv;
        Tangent = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
    }
}
