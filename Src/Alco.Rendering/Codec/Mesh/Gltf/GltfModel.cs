using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

/// <summary>
/// A decoded glTF scene: meshes with GPU-ready vertex/index data in native memory,
/// materials, images and a flattened list of draw items with engine-space world transforms.
/// <br/>Coordinates are converted from the glTF right-handed +Y-up convention to the
/// engine's left-handed +Z-up convention during decoding; triangle winding is preserved.
/// <br/>Dispose to free the native vertex/index buffers.
/// </summary>
public sealed unsafe class GltfModel : AutoDisposable
{
    private readonly GltfPrimitive[] _primitives;

    /// <summary>The decoded meshes.</summary>
    public IReadOnlyList<GltfMesh> Meshes { get; }

    /// <summary>The decoded materials.</summary>
    public IReadOnlyList<GltfMaterial> Materials { get; }

    /// <summary>The decoded images.</summary>
    public IReadOnlyList<GltfImage> Images { get; }

    /// <summary>The flattened renderable instances (mesh + world transform).</summary>
    public IReadOnlyList<GltfDrawItem> DrawItems { get; }

    /// <summary>The number of decoded primitives across all meshes.</summary>
    public int PrimitiveCount => _primitives.Length;

    /// <summary>Scene-space bounds of all draw items (minimum corner).</summary>
    public Vector3 BoundsMin { get; }

    /// <summary>Scene-space bounds of all draw items (maximum corner).</summary>
    public Vector3 BoundsMax { get; }

    internal GltfModel(
        GltfPrimitive[] primitives,
        GltfMesh[] meshes,
        GltfMaterial[] materials,
        GltfImage[] images,
        GltfDrawItem[] drawItems,
        in Vector3 boundsMin,
        in Vector3 boundsMax)
    {
        _primitives = primitives;
        Meshes = meshes;
        Materials = materials;
        Images = images;
        DrawItems = drawItems;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    /// <summary>Get the vertices of a primitive (position/normal/uv/tangent, engine space).</summary>
    /// <param name="primitiveIndex">The global primitive index.</param>
    public ReadOnlySpan<VertexPositionNormalTextureTangent> GetVertices(int primitiveIndex)
    {
        ref GltfPrimitive primitive = ref _primitives[primitiveIndex];
        return new ReadOnlySpan<VertexPositionNormalTextureTangent>(primitive.Vertices, primitive.VertexCount);
    }

    /// <summary>Get the indices of a primitive.</summary>
    /// <param name="primitiveIndex">The global primitive index.</param>
    public ReadOnlySpan<uint> GetIndices(int primitiveIndex)
    {
        ref GltfPrimitive primitive = ref _primitives[primitiveIndex];
        return new ReadOnlySpan<uint>(primitive.Indices, primitive.IndexCount);
    }

    /// <summary>Get the material index of a primitive, -1 when the primitive has no material.</summary>
    /// <param name="primitiveIndex">The global primitive index.</param>
    public int GetMaterialIndex(int primitiveIndex) => _primitives[primitiveIndex].MaterialIndex;

    /// <summary>Get the local-space bounds (minimum corner) of a primitive.</summary>
    /// <param name="primitiveIndex">The global primitive index.</param>
    public Vector3 GetBoundsMin(int primitiveIndex) => _primitives[primitiveIndex].BoundsMin;

    /// <summary>Get the local-space bounds (maximum corner) of a primitive.</summary>
    /// <param name="primitiveIndex">The global primitive index.</param>
    public Vector3 GetBoundsMax(int primitiveIndex) => _primitives[primitiveIndex].BoundsMax;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        for (int i = 0; i < _primitives.Length; i++)
        {
            ref GltfPrimitive primitive = ref _primitives[i];
            if (primitive.Vertices != null)
            {
                NativeMemory.Free(primitive.Vertices);
                primitive.Vertices = null;
            }
            if (primitive.Indices != null)
            {
                NativeMemory.Free(primitive.Indices);
                primitive.Indices = null;
            }
        }
    }
}

/// <summary>
/// A single decoded mesh primitive. Owned by <see cref="GltfModel"/>; access the
/// vertex/index data through the model's span getters.
/// </summary>
public unsafe struct GltfPrimitive
{
    /// <summary>Index into <see cref="GltfModel.Materials"/>, -1 when absent.</summary>
    public int MaterialIndex;

    /// <summary>The number of vertices.</summary>
    public int VertexCount;

    /// <summary>The number of indices.</summary>
    public int IndexCount;

    /// <summary>Local-space bounds (minimum corner), engine space.</summary>
    public Vector3 BoundsMin;

    /// <summary>Local-space bounds (maximum corner), engine space.</summary>
    public Vector3 BoundsMax;

    /// <summary>Native vertex data, owned by the model.</summary>
    public VertexPositionNormalTextureTangent* Vertices;

    /// <summary>Native index data, owned by the model.</summary>
    public uint* Indices;
}
