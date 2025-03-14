using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

public class Cube
{
    public struct RenderDataPerObject
    {
        public Matrix4x4 matrix;
        public ColorFloat color;
    }

    private readonly Material _material;
    private readonly StaticMesh _mesh;
    private RenderDataPerObject _renderData;
    public Transform3D transform;

    public ShapeBox3D Shape
    {
        get => new ShapeBox3D(transform.Position, transform.Scale, transform.Rotation);
    }

    public ColorFloat Color
    {
        get => _renderData.color;
        set => _renderData.color = value;
    }

    public Cube(StaticMesh mesh, Material material)
    {
        _mesh = mesh;
        _material = material;
        transform = Transform3D.Identity;
    }



    public void OnDraw(RenderContext renderer)
    {
        _renderData.matrix = transform.Matrix;
        // renderer already began
        renderer.DrawWithConstant(_mesh, _material, _renderData);
    }
}