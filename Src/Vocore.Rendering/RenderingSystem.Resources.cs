using System.Numerics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    private Texture2D? _textureWhite;

    public Texture2D TextureWhite
    {
        get
        {
            if (_textureWhite == null)
            {
                _textureWhite = CreateTexture2D(4, 4, 0xffffff);
            }
            return _textureWhite;
        }
    }

    private Texture2D? _textureBlack;

    public Texture2D TextureBlack
    {
        get
        {
            if (_textureBlack == null)
            {
                _textureBlack = CreateTexture2D(4, 4, 0);
            }
            return _textureBlack;
        }
    }

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

    private static readonly Vertex2D[] VerticesFullScreenQuad =
    {
        new(new Vector2(-1f, 1f), new Vector2(0, 0)),
        new(new Vector2(1f, 1f), new Vector2(1, 0)),
        new(new Vector2(1f, -1f), new Vector2(1, 1)),
        new(new Vector2(-1f, -1f), new Vector2(0, 1))
    };

    private static readonly ushort[] IndicesFullScreenQuad = { 0, 1, 2, 0, 2, 3 };

    private Mesh? _spriteMesh;
    private Mesh? _trueTypeMesh;
    private Mesh? _fullScreenMesh;

    public Mesh SpriteMesh
    {
        get
        {
            if (_spriteMesh == null)
            {
                _spriteMesh = CreateMesh(VerticesSpriteQuad, IndicesSpriteQuad, "sprite_mesh");
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
                _trueTypeMesh = CreateMesh(VerticesTrueTypeQuad, IndicesTrueTypeQuad, "true_type_mesh");
            }
            return _trueTypeMesh;
        }
    }

    public Mesh FullScreenMesh
    {
        get
        {
            if (_fullScreenMesh == null)
            {
                _fullScreenMesh = CreateMesh(VerticesFullScreenQuad, IndicesFullScreenQuad, "full_screen_mesh");
            }
            return _fullScreenMesh;
        }
    }

    

}