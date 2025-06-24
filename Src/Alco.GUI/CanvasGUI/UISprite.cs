using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>
/// The UI node to draw a sprite.
/// </summary>
public class UISprite : UINode
{
    public static readonly Vector2 DefaultSize = new Vector2(100, 100);
    private Texture2D? _texture;
    private ImageType _imageType = ImageType.Simple;

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

    public Rect UvRect { get; set; } = Rect.One;

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
        Size = DefaultSize;
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
        else
        {
            Size = DefaultSize;
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
            Vertices.EnsureSizeWithoutCopy(16);
            Indices.EnsureSizeWithoutCopy(54);
            UtilsMesh.Populate9SliceMeshData(Vertices.AsSpan(), Indices.AsSpan(), new Vector2(Texture.Width, Texture.Height), Size, Texture.SlicePadding);
        }
    }


    protected override void OnTick(Canvas canvas, float delta)
    {

    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);

        if (ImageType == ImageType.Sliced && Texture != null)
        {
            Transform2D transform = RenderTransform;
            transform.Scale = Vector2.One; // already scaled in mesh
            canvas.DrawSpriteWithCustomMesh(Vertices.AsReadOnlySpan(), Indices.AsReadOnlySpan(), Texture, transform.Matrix, UvRect, Color);
            return;
        }
        canvas.DrawSprite(Texture, RenderTransform.Matrix, UvRect, Color);

    }
}