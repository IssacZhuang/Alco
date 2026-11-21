using System.Numerics;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.Rendering;

/// <summary>
/// GPU geometry buffers for Vertex Pulling: owns a vertex buffer, an index buffer, element counts, and local bounds.
/// </summary>
public sealed class ModelGeometry : AutoDisposable
{
    /// <summary>The diagnostic name of this geometry.</summary>
    public string Name { get; }

    /// <summary>The GPU buffer holding unpacked vertex elements for shader vertex pulling.</summary>
    public GpuBuffer VertexBuffer { get; }

    /// <summary>The GPU buffer holding triangle indices.</summary>
    public GpuBuffer IndexBuffer { get; }

    /// <summary>The number of vertices in <see cref="VertexBuffer"/>.</summary>
    public uint VertexCount { get; }

    /// <summary>The number of indices in <see cref="IndexBuffer"/>.</summary>
    public uint IndexCount { get; }

    /// <summary>The element type stored in <see cref="IndexBuffer"/>.</summary>
    public IndexType IndexType { get; }

    /// <summary>The minimum corner of the geometry-local axis-aligned bounding box.</summary>
    public Vector3 LocalBoundsMin { get; }

    /// <summary>The maximum corner of the geometry-local axis-aligned bounding box.</summary>
    public Vector3 LocalBoundsMax { get; }

    /// <summary>Initializes a geometry wrapper taking ownership of existing GPU buffers.</summary>
    /// <param name="vertexBuffer">The GPU vertex buffer.</param>
    /// <param name="indexBuffer">The GPU index buffer.</param>
    /// <param name="vertexCount">The number of vertices.</param>
    /// <param name="indexCount">The number of indices.</param>
    /// <param name="indexType">The index element type.</param>
    /// <param name="localBoundsMin">The local bounding box minimum.</param>
    /// <param name="localBoundsMax">The local bounding box maximum.</param>
    /// <param name="name">Optional diagnostic name.</param>
    public ModelGeometry(
        GpuBuffer vertexBuffer,
        GpuBuffer indexBuffer,
        uint vertexCount,
        uint indexCount,
        IndexType indexType,
        in Vector3 localBoundsMin,
        in Vector3 localBoundsMax,
        string name = "geometry")
    {
        ArgumentNullException.ThrowIfNull(vertexBuffer);
        ArgumentNullException.ThrowIfNull(indexBuffer);

        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        IndexType = indexType;
        LocalBoundsMin = localBoundsMin;
        LocalBoundsMax = localBoundsMax;
        Name = name;
    }

    /// <summary>Uploads vertices and 32-bit indices into GPU buffers for vertex pulling.</summary>
    /// <typeparam name="TVertex">The unmanaged vertex struct type.</typeparam>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="vertices">The vertex data.</param>
    /// <param name="indices">The 32-bit index data.</param>
    /// <param name="localBoundsMin">The local bounding box minimum.</param>
    /// <param name="localBoundsMax">The local bounding box maximum.</param>
    /// <param name="name">Optional diagnostic name.</param>
    /// <returns>A new <see cref="ModelGeometry"/> owning both GPU buffers.</returns>
    public static ModelGeometry Create<TVertex>(
        RenderingSystem system,
        ReadOnlySpan<TVertex> vertices,
        ReadOnlySpan<uint> indices,
        in Vector3 localBoundsMin,
        in Vector3 localBoundsMax,
        string name = "geometry") where TVertex : unmanaged
    {
        return CreateCore(system, vertices, indices, IndexType.UInt32, localBoundsMin, localBoundsMax, name);
    }

    /// <summary>Uploads vertices and 16-bit indices into GPU buffers for vertex pulling.</summary>
    /// <typeparam name="TVertex">The unmanaged vertex struct type.</typeparam>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="vertices">The vertex data.</param>
    /// <param name="indices">The 16-bit index data.</param>
    /// <param name="localBoundsMin">The local bounding box minimum.</param>
    /// <param name="localBoundsMax">The local bounding box maximum.</param>
    /// <param name="name">Optional diagnostic name.</param>
    /// <returns>A new <see cref="ModelGeometry"/> owning both GPU buffers.</returns>
    public static ModelGeometry Create<TVertex>(
        RenderingSystem system,
        ReadOnlySpan<TVertex> vertices,
        ReadOnlySpan<ushort> indices,
        in Vector3 localBoundsMin,
        in Vector3 localBoundsMax,
        string name = "geometry") where TVertex : unmanaged
    {
        return CreateCore(system, vertices, indices, IndexType.UInt16, localBoundsMin, localBoundsMax, name);
    }

    private static ModelGeometry CreateCore<TVertex, TIndex>(
        RenderingSystem system,
        ReadOnlySpan<TVertex> vertices,
        ReadOnlySpan<TIndex> indices,
        IndexType indexType,
        in Vector3 localBoundsMin,
        in Vector3 localBoundsMax,
        string name)
        where TVertex : unmanaged
        where TIndex : unmanaged
    {
        ArgumentNullException.ThrowIfNull(system);
        if (vertices.IsEmpty)
        {
            throw new ArgumentException("Vertex span cannot be empty.", nameof(vertices));
        }
        if (indices.IsEmpty)
        {
            throw new ArgumentException("Index span cannot be empty.", nameof(indices));
        }

        GpuBuffer vertexBuffer = system.CreateBuffer(vertices);
        try
        {
            GpuBuffer indexBuffer = system.CreateBuffer(indices);
            return new ModelGeometry(
                vertexBuffer,
                indexBuffer,
                (uint)vertices.Length,
                (uint)indices.Length,
                indexType,
                localBoundsMin,
                localBoundsMax,
                name);
        }
        catch
        {
            if (!vertexBuffer.IsDestroyed)
            {
                vertexBuffer.Destroy();
            }
            throw;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        if (!VertexBuffer.IsDestroyed)
        {
            VertexBuffer.Destroy();
        }
        if (!IndexBuffer.IsDestroyed)
        {
            IndexBuffer.Destroy();
        }
    }
}
