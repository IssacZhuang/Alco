using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>
/// The UI node to draw a sprite.
/// </summary>
public class UISprite : UINode
{
    private Texture2D? _texture;
    private ImageType _imageType = ImageType.Simple;
    private Vector2 _tilingScale = Vector2.One;

    private ArrayBuffer<Vertex>? _vertices = null;
    private ArrayBuffer<ushort>? _indices = null;

    /// <summary>
    /// The texture of the sprite. The white quad will be rendered if it is null.
    /// </summary>
    /// <value></value>
    public Texture2D? Texture
    {
        get => _texture;
        set
        {
            if (_texture == value)
            {
                return;
            }
            _texture = value;
            SetRenderDataDirty();
        }
    }

    public ImageType ImageType
    {
        get => _imageType;
        set
        {
            if (_imageType == value)
            {
                return;
            }
            _imageType = value;
            SetRenderDataDirty();
        }
    }

    /// <summary>
    /// The color of the sprite.
    /// </summary>
    /// <returns></returns>
    public ColorFloat Color { get; set; } = new ColorFloat(1, 1, 1, 1);

    /// <summary>
    /// The UV rectangle that defines the portion of the texture to use.
    /// </summary>
    public Rect UvRect { get; set; } = Rect.One;

    /// <summary>
    /// The tiling scale for Tiled image type. Controls how many times the texture repeats.
    /// Default is (1, 1) which means the texture repeats based on size/texture dimensions.
    /// </summary>
    public Vector2 TilingScale
    {
        get => _tilingScale;
        set
        {
            if (_tilingScale == value)
            {
                return;
            }
            _tilingScale = value;
            SetRenderDataDirty();
        }
    }

    protected ArrayBuffer<Vertex> Vertices
    {
        get => _vertices ??= new ArrayBuffer<Vertex>();
    }
    protected ArrayBuffer<ushort> Indices
    {
        get => _indices ??= new ArrayBuffer<ushort>();
    }

    public UISprite()
    {
        
    }

    /// <summary>
    /// Set the size of the sprite to the size of the texture.
    /// </summary>
    public void SetNativeSize()
    {
        if (Texture != null)
        {
            Size = new Vector2(Texture.Width, Texture.Height);
        }
    }

    public void SetSprite(Sprite sprite)
    {
        Texture = sprite.Texture;
        UvRect = sprite.UvRect;
    }

    protected override void OnUpdateRenderData(Canvas canvas, float delta)
    {
        if (ImageType == ImageType.Sliced && Texture != null)
        {
            Vertices.SetSizeWithoutCopy(16);
            Indices.SetSizeWithoutCopy(54);
            UtilsMesh.Populate9SliceMeshData(Vertices.AsSpan(0, 16), Indices.AsSpan(0, 54), new Vector2(Texture.Width, Texture.Height), Size, Texture.SlicePadding);
        }
        else if (ImageType == ImageType.Tiled && Texture != null)
        {
            Vertices.SetSizeWithoutCopy(4);
            Indices.SetSizeWithoutCopy(6);
            UtilsMesh.PopulateTiledMeshData(Vertices.AsSpan(0, 4), Indices.AsSpan(0, 6), new Vector2(Texture.Width, Texture.Height), Size, TilingScale);
        }
    }


    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);

        if (ImageType == ImageType.Sliced && Texture != null)
        {
            Transform2D transform = RenderTransform;
            transform.Scale = Vector2.One; // already scaled in mesh
            canvas.DrawSpriteWithCustomMesh(Vertices.AsSpan(0, 16), Indices.AsSpan(0, 54), Texture, transform.Matrix, UvRect, Color);
            return;
        }
        else if (ImageType == ImageType.Tiled && Texture != null)
        {
            Transform2D transform = RenderTransform;
            transform.Scale = Vector2.One; // already scaled in mesh
            canvas.DrawSpriteWithCustomMesh(Vertices.AsSpan(0, 4), Indices.AsSpan(0, 6), Texture, transform.Matrix, UvRect, Color);
            return;
        }
        canvas.DrawSprite(Texture, RenderTransform.Matrix, UvRect, Color);

    }
}