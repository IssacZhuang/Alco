using System.Numerics;

namespace Alco.Rendering;

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

    private static readonly Vertex[] VerticesCenteredSpriteQuad =
   {
        new(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0)),
        new(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0)),
        new(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1)),
        new(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1))
    };

    private static readonly ushort[] IndicesCenteredSpriteQuad = { 0, 1, 2, 0, 2, 3 };

    private static readonly Vertex[] VerticesMidUpSpriteQuad =
    {
        new(new Vector3(-0.5f, 1, 0), new Vector2(0, 0)),
        new(new Vector3(0.5f, 1, 0), new Vector2(1, 0)),
        new(new Vector3(0.5f, 0, 0), new Vector2(1, 1)),
        new(new Vector3(-0.5f, 0, 0), new Vector2(0, 1))

    };

    private static readonly ushort[] IndicesMidUpSpriteQuad = { 0, 1, 2, 0, 2, 3 };

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

    private Mesh? _meshCenteredSprite;
    private Mesh? _meshMidUpSprite;
    private Mesh? _meshTrueType;
    private Mesh? _mehsFullScreen;
    private Mesh? _meshCube;
    

    public Mesh MeshCenteredSprite
    {
        get
        {
            if (_meshCenteredSprite == null)
            {
                _meshCenteredSprite = CreateMesh(VerticesCenteredSpriteQuad, IndicesCenteredSpriteQuad, "centered_sprite_mesh");
            }
            return _meshCenteredSprite;

        }
    }

    public Mesh MeshMidUpSprite
    {
        get
        {
            if (_meshMidUpSprite == null)
            {
                _meshMidUpSprite = CreateMesh(VerticesMidUpSpriteQuad, IndicesMidUpSpriteQuad, "mid_up_sprite_mesh");
            }
            return _meshMidUpSprite;

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