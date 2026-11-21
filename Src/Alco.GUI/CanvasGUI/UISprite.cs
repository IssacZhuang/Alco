using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>UI node that draws a solid, stretched, 9-sliced, or tiled sprite image.</summary>
public class UIImage : UINode
{
    private Texture2D? _texture;
    private ImageType _imageType = ImageType.Simple;
    private Vector2 _tilingScale = Vector2.One;

    /// <summary>The texture of the sprite; null draws a solid-colored quad.</summary>
    public Texture2D? Texture
    {
        get => _texture;
        set
        {
            if (_texture == value)
                return;
            _texture = value;
            SetRenderDataDirty();
        }
    }

    /// <summary>Optional custom <c>AlcoGUI_Pass</c> surface material overriding default shading.</summary>
    public Material? Material { get; set; }

    /// <summary>True when <see cref="Texture"/> stores premultiplied alpha texels.</summary>
    public bool Premultiplied { get; set; }

    /// <summary>True to sample <see cref="Texture"/> using nearest-neighbor filtering instead of linear.</summary>
    public bool NearestFilter { get; set; }

    /// <summary>Explicit 9-slice borders in texels (left, top, right, bottom); falls back to <see cref="Texture2D.SlicePadding"/> when zero.</summary>
    public Vector4 SliceBorders { get; set; }

    /// <summary>The draw mode used for the image (simple, 9-sliced, or tiled).</summary>
    public ImageType ImageType
    {
        get => _imageType;
        set
        {
            if (_imageType == value)
                return;
            _imageType = value;
            SetRenderDataDirty();
        }
    }

    /// <summary>Normalized UV sub-rectangle sampled from <see cref="Texture"/>.</summary>
    public Rect UvRect { get; set; } = Rect.One;

    /// <summary>Tiling repeat scale multiplier when <see cref="ImageType"/> is <see cref="ImageType.Tiled"/>.</summary>
    public Vector2 TilingScale
    {
        get => _tilingScale;
        set
        {
            if (_tilingScale == value)
                return;
            _tilingScale = value;
            SetRenderDataDirty();
        }
    }

    /// <summary>Creates an image UI node.</summary>
    public UIImage()
    {
    }

    /// <summary>Resizes this node to match the texel dimensions of <see cref="Texture"/>.</summary>
    public void SetNativeSize()
    {
        if (Texture != null)
            Size = new Vector2(Texture.Width, Texture.Height);
    }

    /// <summary>Assigns both <see cref="Texture"/> and <see cref="UvRect"/> from <paramref name="sprite"/>.</summary>
    /// <param name="sprite">Sprite region to display.</param>
    public void SetSprite(Sprite sprite)
    {
        Texture = sprite.Texture;
        UvRect = sprite.UvRect;
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);

        if (ImageType == ImageType.Sliced && Texture != null)
        {
            Vector4 borders = SliceBorders == default
                ? new Vector4(Texture.SlicePadding.Left, Texture.SlicePadding.Top, Texture.SlicePadding.Right, Texture.SlicePadding.Bottom)
                : SliceBorders;
            canvas.DrawNineSlice(Texture, RenderTransform.Matrix, UvRect, RenderColor, borders, Premultiplied, NearestFilter, Material);
            return;
        }

        if (ImageType == ImageType.Tiled && Texture != null)
        {
            canvas.DrawTiledSprite(Texture, RenderTransform.Matrix, UvRect, TilingScale, RenderColor, Premultiplied, Material);
            return;
        }

        canvas.DrawSprite(Texture, RenderTransform.Matrix, UvRect, RenderColor, Premultiplied, NearestFilter, Material);
    }
}
