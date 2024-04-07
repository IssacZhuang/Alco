using System.Numerics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    private static readonly Vertex2D[] VerticesSpriteQuad =
   {
        new(new Vector2(-0.5f, 0.5f), new Vector2(0, 0)),
        new(new Vector2(0.5f, 0.5f), new Vector2(1, 0)),
        new(new Vector2(0.5f, -0.5f), new Vector2(1, 1)),
        new(new Vector2(-0.5f, -0.5f), new Vector2(0, 1))
    };

    private static readonly ushort[] IndicesSpriteQuad = { 0, 1, 2, 0, 2, 3 };

    private static readonly Vertex2D[] VerticesTrueTypeQuad =
    {
        new (new Vector2(0, 0),new Vector2(0, 0)),
        new ( new Vector2(1, 0),  new Vector2(1, 0)),
        new ( new Vector2(1, -1),  new Vector2(1, 1)),
        new ( new Vector2(0, -1),  new Vector2(0, 1))
    };

    private static readonly ushort[] IndicesTrueTypeQuad = { 0, 1, 2, 0, 2, 3 };

    private Mesh? _spriteMesh;
    private Mesh? _trueTypeMesh;

    public Mesh SpriteMesh
    {
        get
        {
            if (_spriteMesh == null)
            {
                _spriteMesh = CreateSpriteMesh();
            }
            return _spriteMesh;
        }
    }

    public Mesh TrueTypeMesh
    {
        get
        {
            if (_trueTypeMesh == null)
            {
                _trueTypeMesh = CreateTrueTypeMesh();
            }
            return _trueTypeMesh;
        }
    }

    private Mesh CreateSpriteMesh()
    {
        return CreateMesh(VerticesSpriteQuad, IndicesSpriteQuad);
    }

    private Mesh CreateTrueTypeMesh()
    {
        return CreateMesh(VerticesTrueTypeQuad, IndicesTrueTypeQuad);
    }

}