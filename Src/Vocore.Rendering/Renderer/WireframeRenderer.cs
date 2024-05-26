using System.Numerics;

namespace Vocore.Rendering;

public class WireframeRenderer : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private NativeArrayList<Vector3> _vertices;
    private NativeArrayList<short> _indices;

    private GraphicsBuffer? _vertexBuffer;
    private GraphicsBuffer? _indexBuffer;

    public ReadOnlySpan<Vector3> Vertices
    {
        get => _vertices.MemoryRef.Span;
    }

    public ReadOnlySpan<short> Indices
    {
        get => _indices.MemoryRef.Span;
    }

    internal WireframeRenderer(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
        _vertices = new NativeArrayList<Vector3>();
        _indices = new NativeArrayList<short>();
    }

    public void AddLine(Vector3 start, Vector3 end)
    {
        _vertices.Add(start);
        _vertices.Add(end);

        short index = (short)(_vertices.Count - 2);
        _indices.Add(index);
        _indices.Add((short)(index + 1));
    }

    public void Clear()
    {
        _vertices.Clear();
        _indices.Clear();
    }

    private unsafe void EnsureGraphicsBuffer()
    {
        uint vertexBufferSize = (uint)(_vertices.Count * sizeof(Vector3));
        if (_vertexBuffer == null || _vertexBuffer.Size < vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBuffer = _renderingSystem.CreateGraphicsBuffer(vertexBufferSize, "wireframe_vertex_buffer");
        }

        uint indexBufferSize = (uint)(_indices.Count * sizeof(short));
        if (_indexBuffer == null || _indexBuffer.Size < indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBuffer = _renderingSystem.CreateGraphicsBuffer(indexBufferSize, "wireframe_index_buffer");
        }
    }

    protected override void Dispose(bool disposing)
    {
        _vertices.Dispose();
        _indices.Dispose();
    }
}