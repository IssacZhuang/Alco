using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

namespace _33_LLM;

/// <summary>
/// A simple cube entity for rendering.
/// </summary>
public class Cube
{
    /// <summary>
    /// Per-object data for rendering.
    /// </summary>
    public struct RenderDataPerObject
    {
        public Matrix4x4 matrix;
        public ColorFloat color;
    }

    private readonly Material _material;
    private readonly Mesh _mesh;
    private RenderDataPerObject _renderData;
    
    /// <summary>
    /// The transform of the cube.
    /// </summary>
    public Transform3D transform;

    /// <summary>
    /// Gets the collision shape of the cube.
    /// </summary>
    public ShapeBox3D Shape
    {
        get => new ShapeBox3D(transform.Position, transform.Scale, transform.Rotation);
    }

    /// <summary>
    /// Gets or sets the color of the cube.
    /// </summary>
    public ColorFloat Color
    {
        get => _renderData.color;
        set => _renderData.color = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Cube"/> class.
    /// </summary>
    /// <param name="mesh">The mesh to use.</param>
    /// <param name="material">The material to use.</param>
    public Cube(Mesh mesh, Material material)
    {
        _mesh = mesh;
        _material = material;
        transform = Transform3D.Identity;
    }

    /// <summary>
    /// Draws the cube using the provided renderer.
    /// </summary>
    /// <param name="renderer">The render context.</param>
    public void OnDraw(RenderContext renderer)
    {
        _renderData.matrix = transform.Matrix;
        renderer.DrawWithConstant(_mesh, _material, _renderData);
    }
}

