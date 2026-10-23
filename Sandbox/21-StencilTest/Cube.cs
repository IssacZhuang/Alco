using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

/// <summary>A cube entity with a box collider that draws itself with a per-object constant.</summary>
public class Cube
{
    /// <summary>Per-object constant data uploaded for each draw.</summary>
    public struct RenderDataPerObject
    {
        public Matrix4x4 matrix;
        public ColorFloat color;
    }

    private readonly GraphicsMaterial _material;
    private readonly Mesh _mesh;
    private RenderDataPerObject _renderData;
    public Transform3D transform;

    /// <summary>Box collider derived from the cube's current transform.</summary>
    public ShapeBox3D Shape
    {
        get => new ShapeBox3D(transform.Position, transform.Scale, transform.Rotation);
    }

    /// <summary>Color used when drawing the cube.</summary>
    public ColorFloat Color
    {
        get => _renderData.color;
        set => _renderData.color = value;
    }

    /// <summary>Initializes a cube with its mesh and material.</summary>
    /// <param name="mesh">Mesh rendered for the cube.</param>
    /// <param name="material">Material used for drawing.</param>
    public Cube(Mesh mesh, GraphicsMaterial material)
    {
        _mesh = mesh;
        _material = material;
        transform = Transform3D.Identity;
    }



    /// <summary>Draws the cube into an already-begun render context.</summary>
    /// <param name="renderer">Render context to record the draw into.</param>
    public void OnDraw(IRenderContext renderer)
    {
        _renderData.matrix = transform.Matrix;
        // renderer already began
        renderer.DrawWithConstant(_mesh, _material, _renderData);
    }
}