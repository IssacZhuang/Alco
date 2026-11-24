using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>UI node that draws a solid, stretched, 9-sliced, or tiled sprite image.</summary>
public class UIImage : UINode
{
    private Texture? _texture;
    private Sprite? _sprite;
    private ImageType _imageType = ImageType.Simple;
    private Vector2 _tilingScale = Vector2.One;
    private Rect _uvRect = Rect.One;

    /// <summary>The GPU texture drawn by this node; null draws a solid-colored quad.</summary>
    public Texture? Texture
    {
        get => _texture;
        set
        {
            if (_texture == value)
                return;
            _texture = value;
            if (_sprite != null && !ReferenceEquals(_sprite.Texture, value))
                _sprite = null;
            SetRenderDataDirty();
        }
    }

    /// <summary>Optional custom <c>AlcoGUI_Pass</c> surface material overriding default shading.</summary>
    public Material? Material { get; set; }

    /// <summary>True when <see cref="Texture"/> stores premultiplied alpha texels.</summary>
    public bool Premultiplied { get; set; }

    /// <summary>True to sample <see cref="Texture"/> using nearest-neighbor filtering instead of linear.</summary>
    public bool NearestFilter { get; set; }

    /// <summary>9-slice borders in texels (left, top, right, bottom).</summary>
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
    public Rect UvRect
    {
        get => _uvRect;
        set
        {
            _uvRect = value;
            SetRenderDataDirty();
        }
    }

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

    /// <summary>Creates an image UI node initialized from an <see cref="Image"/> asset.</summary>
    /// <param name="image">Optional image asset supplying texture and import parameters.</param>
    public UIImage(Image? image)
    {
        if (image != null)
        {
            SetImage(image);
        }
    }

    /// <summary>Resizes this node to match the texel dimensions of the currently displayed sprite region.</summary>
    public void SetNativeSize()
    {
        if (Texture != null)
        {
            Size = new Vector2(Texture.Extent.X * _uvRect.Size.X, Texture.Extent.Y * _uvRect.Size.Y);
        }
    }

    /// <summary>Assigns texture, UVs, 9-slice borders, filter mode, and alpha mode from an <see cref="Image"/> asset.</summary>
    /// <param name="image">Image asset to apply, or null to clear.</param>
    public void SetImage(Image? image)
    {
        SetSprite(image?.DefaultSprite);
    }

    /// <summary>Assigns texture, UVs, 9-slice borders, filter mode, and alpha mode from a named sprite of an <see cref="Image"/> asset.</summary>
    /// <param name="image">Image asset containing the sprite.</param>
    /// <param name="spriteName">Named sprite inside <paramref name="image"/> (falls back to default sprite when absent).</param>
    public void SetImage(Image image, string spriteName)
    {
        ArgumentNullException.ThrowIfNull(image);
        SetSprite(image.GetSprite(spriteName));
    }

    /// <summary>Assigns <see cref="Texture"/>, <see cref="UvRect"/>, <see cref="SliceBorders"/>, <see cref="NearestFilter"/>, and <see cref="Premultiplied"/> from <paramref name="sprite"/>.</summary>
    /// <param name="sprite">Sprite region to display, or null to clear.</param>
    public void SetSprite(Sprite? sprite)
    {
        if (sprite == null)
        {
            _sprite = null;
            Texture = null;
            UvRect = Rect.One;
            SliceBorders = default;
            NearestFilter = false;
            Premultiplied = false;
            SetRenderDataDirty();
            return;
        }

        _sprite = sprite;
        Texture = sprite.Texture;
        UvRect = sprite.UvRect;
        SliceBorders = new Vector4(sprite.SlicePadding.Left, sprite.SlicePadding.Top, sprite.SlicePadding.Right, sprite.SlicePadding.Bottom);
        NearestFilter = sprite.FilterMode == Filter.Nearest;
        Premultiplied = sprite.IsPremultiplied;
        SetRenderDataDirty();
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);

        if (ImageType == ImageType.Sliced && Texture != null)
        {
            canvas.DrawNineSlice(Texture, RenderTransform.Matrix, UvRect, RenderColor, SliceBorders, Premultiplied, NearestFilter, Material);
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
