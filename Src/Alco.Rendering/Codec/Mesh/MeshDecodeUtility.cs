using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// Static facade for mesh decoding and vertex utility. Dispatchs to format-specific
/// decoders and provides shared tangent computation. All decode methods are thread-safe.
/// Returned pointers are caller-owned and must be freed via <c>NativeMemory.Free</c>.
/// </summary>
public static unsafe class MeshDecodeUtility
{
    /// <summary>
    /// Decode OBJ data into vertex and index buffers.
    /// </summary>
    /// <param name="data">Raw OBJ file bytes.</param>
    /// <param name="vertexCount">Number of decoded vertices.</param>
    /// <param name="indices">Pointer to decoded index data. Caller must free via <c>NativeMemory.Free</c>.</param>
    /// <param name="indexCount">Number of decoded indices.</param>
    /// <returns>Pointer to vertex data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="MeshDecodeException">Invalid or unsupported OBJ data.</exception>
    public static VertexPBR* DecodeObj(ReadOnlySpan<byte> data, out int vertexCount, out uint* indices, out int indexCount)
        => ObjDecoder.Decode(data, out vertexCount, out indices, out indexCount);

    /// <summary>
    /// Auto-detect mesh format and decode into vertex and index buffers.
    /// Currently only supports OBJ format.
    /// </summary>
    /// <param name="data">Raw mesh file bytes.</param>
    /// <param name="vertexCount">Number of decoded vertices.</param>
    /// <param name="indices">Pointer to decoded index data. Caller must free via <c>NativeMemory.Free</c>.</param>
    /// <param name="indexCount">Number of decoded indices.</param>
    /// <returns>Pointer to vertex data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="MeshDecodeException">Unrecognized format or corrupt data.</exception>
    public static VertexPBR* DecodeAuto(ReadOnlySpan<byte> data, out int vertexCount, out uint* indices, out int indexCount)
    {
        // Currently only OBJ is supported; defer to the OBJ decoder directly.
        return ObjDecoder.Decode(data, out vertexCount, out indices, out indexCount);
    }

    // ── Tangent computation ──

    /// <summary>
    /// Compute per-vertex tangents from triangle UVs (Lengyel's method): tangents and
    /// bitangents accumulate per triangle, then each tangent is orthogonalized against
    /// the normal and gets its bitangent sign from the accumulated UV handedness.
    /// The <paramref name="vertices"/> span is mutated in-place — each vertex's
    /// <see cref="VertexPBR.Tangent"/> field is overwritten with the result.
    /// </summary>
    /// <param name="vertices">The vertex span to read positions/normals/UVs from and write tangents to.</param>
    /// <param name="indices">Triangle index data (indexCount must be a multiple of 3).</param>
    internal static void ComputeTangents(Span<VertexPBR> vertices, ReadOnlySpan<uint> indices)
    {
        int vertexCount = vertices.Length;
        Span<Vector3> bitangents = new Vector3[vertexCount];
        bitangents.Clear();

        int triangleCount = indices.Length / 3;
        for (int t = 0; t < triangleCount; t++)
        {
            int i0 = (int)indices[t * 3];
            int i1 = (int)indices[t * 3 + 1];
            int i2 = (int)indices[t * 3 + 2];

            Vector3 edge1 = vertices[i1].Position - vertices[i0].Position;
            Vector3 edge2 = vertices[i2].Position - vertices[i0].Position;
            Vector2 duv1 = vertices[i1].UV - vertices[i0].UV;
            Vector2 duv2 = vertices[i2].UV - vertices[i0].UV;

            float det = duv1.X * duv2.Y - duv2.X * duv1.Y;
            if (MathF.Abs(det) < 1e-20f)
            {
                continue;
            }
            float f = 1.0f / det;
            Vector3 tangent = (duv2.Y * edge1 - duv1.Y * edge2) * f;
            Vector3 bitangent = (duv1.X * edge2 - duv2.X * edge1) * f;

            AccumulateTangent(ref vertices[i0].Tangent, tangent);
            AccumulateTangent(ref vertices[i1].Tangent, tangent);
            AccumulateTangent(ref vertices[i2].Tangent, tangent);
            bitangents[i0] += bitangent;
            bitangents[i1] += bitangent;
            bitangents[i2] += bitangent;
        }

        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 normal = vertices[i].Normal;
            Vector3 accumulated = new(vertices[i].Tangent.X, vertices[i].Tangent.Y, vertices[i].Tangent.Z);

            // Gram-Schmidt orthogonalization; fall back to an arbitrary orthogonal
            // when no usable tangent accumulated (degenerate UVs everywhere).
            Vector3 tangent = accumulated - normal * Vector3.Dot(normal, accumulated);
            if (tangent.LengthSquared() > 1e-12f)
            {
                tangent = Vector3.Normalize(tangent);
            }
            else
            {
                tangent = ArbitraryOrthogonal(normal);
            }

            float sign = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0.0f ? -1.0f : 1.0f;
            vertices[i].Tangent = new Vector4(tangent, sign);
        }
    }

    /// <summary>
    /// Assign an arbitrary orthogonal tangent to every vertex (no UVs to derive one from).
    /// </summary>
    public static void SetDefaultTangents(Span<VertexPBR> vertices)
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].Tangent = new Vector4(ArbitraryOrthogonal(vertices[i].Normal), 1.0f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AccumulateTangent(ref Vector4 target, Vector3 tangent)
    {
        target = new Vector4(target.X + tangent.X, target.Y + tangent.Y, target.Z + tangent.Z, 0.0f);
    }

    private static Vector3 ArbitraryOrthogonal(in Vector3 normal)
    {
        Vector3 axis = MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 orthogonal = Vector3.Cross(normal, axis);
        return orthogonal.LengthSquared() > 1e-12f ? Vector3.Normalize(orthogonal) : Vector3.UnitX;
    }
}
