
namespace Alco.Rendering;

/// <summary>
/// A utility class for merging multiple meshes into a single mesh by combining their vertices and indices.
/// </summary>
/// <typeparam name="TVertex">The vertex type, must be unmanaged.</typeparam>
public class MeshMerger<TVertex> where TVertex : unmanaged
{
    private readonly ArrayBuffer<TVertex> _vertices = new ArrayBuffer<TVertex>();
    private readonly ArrayBuffer<uint> _indices = new ArrayBuffer<uint>();

    private int _vertexCount = 0;
    private int _indexCount = 0;

    /// <summary>
    /// Gets a span representing the merged vertices.
    /// </summary>
    public Span<TVertex> Vertices => _vertices.AsSpan(0, _vertexCount);
    
    /// <summary>
    /// Gets a span representing the merged indices.
    /// </summary>
    public Span<uint> Indices => _indices.AsSpan(0, _indexCount);

    /// <summary>
    /// Adds a mesh to the merger by appending its vertices and indices.
    /// The indices are automatically adjusted to account for the existing vertex count.
    /// </summary>
    /// <param name="vertices">The vertices of the mesh to add.</param>
    /// <param name="indices">The indices of the mesh to add.</param>
    public void AddMesh(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices)
    {
        if (vertices.IsEmpty && indices.IsEmpty)
            return;

        // Calculate new sizes
        int newVertexCount = _vertexCount + vertices.Length;
        int newIndexCount = _indexCount + indices.Length;

        // Ensure buffers have enough capacity
        _vertices.SetSize(newVertexCount);
        _indices.SetSize(newIndexCount);

        // Copy vertices
        if (!vertices.IsEmpty)
        {
            vertices.CopyTo(_vertices.AsSpan(_vertexCount, vertices.Length));
        }

        // Copy and adjust indices
        if (!indices.IsEmpty)
        {
            var targetIndices = _indices.AsSpan(_indexCount, indices.Length);
            for (int i = 0; i < indices.Length; i++)
            {
                targetIndices[i] = indices[i] + (uint)_vertexCount;
            }
        }

        // Update counts
        _vertexCount = newVertexCount;
        _indexCount = newIndexCount;
    }

    /// <summary>
    /// Clears all merged mesh data, resetting the merger to its initial state.
    /// </summary>
    public void Clear()
    {
        _vertexCount = 0;
        _indexCount = 0;
        _vertices.SetSize(0);
        _indices.SetSize(0);
    }
}