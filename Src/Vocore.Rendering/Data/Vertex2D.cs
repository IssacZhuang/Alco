using System.Numerics;
using System.Runtime.InteropServices;

namespace Vocore.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex2D
{
    public Vertex2D(Vector2 position, Vector2 uv)
    {
        this.position = position;
        this.uv = uv;
    }
    public Vector2 position;
    public Vector2 uv;
}