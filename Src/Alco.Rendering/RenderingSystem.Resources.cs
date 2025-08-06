using System.Numerics;

namespace Alco.Rendering;

// resource factory

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

    private static readonly Vertex[] Vertices9SliceSpriteQuad =
    {
        // top row
        new(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0)),    //[0,0] top left
        new(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0)),        //[1,0] take offset left
        new(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0)),        //[2,0] take offset right
        new(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0)),     //[3,0] top right

        // middle row
        new(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0)),       //[0,1] take offset top 
        new(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0)),           //[1,1] take offset top left
        new(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0)),           //[2,1] take offset top right
        new(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0)),        //[3,1] take offset top

        // middle row 2
        new(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1)),       //[0,2] take offset bottom
        new(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1)),           //[1,2] take offset bottom left
        new(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1)),           //[2,2] take offset bottom right
        new(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1)),        //[3,2] take offset bottom

        // bottom row
        new(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1)),   //[0,3] bottom left
        new(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1)),       //[1,3] take offset left
        new(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1)),       //[2,3] take offset right
        new(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1)),    //[3,3] bottom right
    };

    private static readonly ushort[] Indices9SliceSpriteQuad =
    {
        // Top-left section
        0, 1, 5, 0, 5, 4,
        // Top-middle section
        1, 2, 6, 1, 6, 5,
        // Top-right section
        2, 3, 7, 2, 7, 6,
        // Middle-left section
        4, 5, 9, 4, 9, 8,
        // Middle-middle section
        5, 6, 10, 5, 10, 9,
        // Middle-right section
        6, 7, 11, 6, 11, 10,
        // Bottom-left section
        8, 9, 13, 8, 13, 12,
        // Bottom-middle section
        9, 10, 14, 9, 14, 13,
        // Bottom-right section
        10, 11, 15, 10, 15, 14
    };

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
    private Mesh? _mesh9SliceSprite;
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
                _meshCenteredSprite = CreatePrimitiveMesh<Vertex>(VerticesCenteredSpriteQuad, IndicesCenteredSpriteQuad, "centered_sprite_mesh");
            }
            return _meshCenteredSprite;

        }
    }

    public Mesh Mesh9SliceSprite
    {
        get
        {
            if (_mesh9SliceSprite == null)
            {
                _mesh9SliceSprite = CreatePrimitiveMesh<Vertex>(Vertices9SliceSpriteQuad, Indices9SliceSpriteQuad, "9_slice_sprite_mesh");
            }
            return _mesh9SliceSprite;
        }
    }


    public Mesh MeshMidUpSprite
    {
        get
        {
            if (_meshMidUpSprite == null)
            {
                _meshMidUpSprite = CreatePrimitiveMesh<Vertex>(VerticesMidUpSpriteQuad, IndicesMidUpSpriteQuad, "mid_up_sprite_mesh");
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
                _meshTrueType = CreatePrimitiveMesh<Vertex>(VerticesTrueTypeQuad, IndicesTrueTypeQuad, "true_type_mesh");
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
                _mehsFullScreen = CreatePrimitiveMesh<Vertex>(VerticesFullScreenQuad, IndicesFullScreenQuad, "full_screen_mesh");
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
                _meshCube = CreatePrimitiveMesh<Vertex>(VerticesBox, IndicesBox, "box_mesh");
            }
            return _meshCube;
        }
    }

}