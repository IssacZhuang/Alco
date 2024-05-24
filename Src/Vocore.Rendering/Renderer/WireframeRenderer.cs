using System.Numerics;

namespace Vocore.Rendering;

public class WireframeRenderer : AutoDisposable
{
    private NativeArrayList<Vector3> _vertices;
    private NativeArrayList<short> _indices;

    public ReadOnlySpan<Vector3> Vertices
    {
        get => _vertices.MemoryRef.Span;
    }

    public ReadOnlySpan<short> Indices
    {
        get => _indices.MemoryRef.Span;
    }

    public WireframeRenderer()
    {
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

    protected override void Dispose(bool disposing)
    {
        _vertices.Dispose();
        _indices.Dispose();
    }
}