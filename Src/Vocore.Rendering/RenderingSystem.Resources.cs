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

    private static readonly Vertex[] VerticesSpriteQuad =
   {
        new(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0)),
        new(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0)),
        new(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1)),
        new(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1))
    };

    private static readonly ushort[] IndicesSpriteQuad = { 0, 1, 2, 0, 2, 3 };

    private static readonly Vertex[] VerticesTrueTypeQuad =
    {
        new (new Vector3(0, 0, 0),new Vector2(0, 0)),
        new ( new Vector3(1, 0, 0),  new Vector2(1, 0)),
        new ( new Vector3(1, -1, 0),  new Vector2(1, 1)),
        new ( new Vector3(0, -1, 0),  new Vector2(0, 1))
    };

    private static readonly ushort[] IndicesTrueTypeQuad = { 0, 1, 2, 0, 2, 3 };

    private static readonly Vertex[] VerticesFullScreenQuad =
    {
        new(new Vector3(-1f, 1f, 0), new Vector2(0, 0)),
        new(new Vector3(1f, 1f, 0), new Vector2(1, 0)),
        new(new Vector3(1f, -1f, 0), new Vector2(1, 1)),
        new(new Vector3(-1f, -1f, 0), new Vector2(0, 1))
    };

    private static readonly ushort[] IndicesFullScreenQuad = { 0, 1, 2, 0, 2, 3 };

    private static readonly Vertex[] VerticesBox =
    {
        new(new Vector3(-0.5f, 0.5f, 0.5f), new Vector2(0, 0)),
        new(new Vector3(0.5f, 0.5f, 0.5f), new Vector2(1, 0)),
        new(new Vector3(0.5f, -0.5f, 0.5f), new Vector2(1, 1)),
        new(new Vector3(-0.5f, -0.5f, 0.5f), new Vector2(0, 1)),
        new(new Vector3(-0.5f, 0.5f, -0.5f), new Vector2(0, 0)),
        new(new Vector3(0.5f, 0.5f, -0.5f), new Vector2(1, 0)),
        new(new Vector3(0.5f, -0.5f, -0.5f), new Vector2(1, 1)),
        new(new Vector3(-0.5f, -0.5f, -0.5f), new Vector2(0, 1))
    };

    private static readonly ushort[] IndicesBox =
    {
        0, 1, 2, 0, 2, 3,
        1, 5, 6, 1, 6, 2,
        5, 4, 7, 5, 7, 6,
        4, 0, 3, 4, 3, 7,
        4, 5, 1, 4, 1, 0,
        3, 2, 6, 3, 6, 7
    };

    private Mesh? _meshSprite;
    private Mesh? _meshTrueType;
    private Mesh? _mehsFullScreen;
    private Mesh? _meshCube;

    public Mesh MeshSprite
    {
        get
        {
            if (_meshSprite == null)
            {
                _meshSprite = CreateMesh(VerticesSpriteQuad, IndicesSpriteQuad, "sprite_mesh");
            }
            return _meshSprite;
        }
    }

    public Mesh MeshTrueType
    {
        get
        {
            if (_meshTrueType == null)
            {
                _meshTrueType = CreateMesh(VerticesTrueTypeQuad, IndicesTrueTypeQuad, "true_type_mesh");
            }
            return _meshTrueType;
        }
    }

    public Mesh MeshFullScreen
    {
        get
        {
            if (_mehsFullScreen == null)
            {
                _mehsFullScreen = CreateMesh(VerticesFullScreenQuad, IndicesFullScreenQuad, "full_screen_mesh");
            }
            return _mehsFullScreen;
        }
    }

    public Mesh MeshCube
    {
        get
        {
            if (_meshCube == null)
            {
                _meshCube = CreateMesh(VerticesBox, IndicesBox, "box_mesh");
            }
            return _meshCube;
        }
    }

}