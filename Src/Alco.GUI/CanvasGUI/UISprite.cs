using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>
/// The UI node to draw a sprite.
/// </summary>
public class UIImage : UINode
{
    private Texture2D? _texture;
    private ImageType _imageType = ImageType.Simple;
    private Vector2 _tilingScale = Vector2.One;

    private MaterialAsset? _material;
    private GraphicsMaterialInstance? _materialInstance;
    private Canvas? _materialCanvas;
    private Texture2D? _maskTexture;
    private Texture2D? _boundMaterialTexture;
    private Texture2D? _boundMaterialMaskTexture;

    private ArrayBuffer<Vertex>? _vertices = null;
    private ArrayBuffer<ushort>? _indices = null;
    private Vector2 _lastMeshSize;

    /// <summary>
    /// The texture of the sprite. Null draws a white quad without a custom
    /// <see cref="Material"/>, or inherits the material asset's texture otherwise.
    /// </summary>
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

    /// <summary>
    /// The material asset whose surface draws this image instead of the plain sprite material. The node's <see cref="Texture"/> and <see cref="MaskTexture"/> override the surface's declared slots; null values inherit the asset's bindings.
    /// </summary>
    public MaterialAsset? Material
    {
        get => _material;
        set
        {
            if (_material == value)
            {
                return;
            }
            _material = value;
            DisposeMaterialInstance();
        }
    }

    /// <summary>
    /// The color mask texture bound to the custom material's mask slot (the surface
    /// family convention). Null inherits the material asset's mask binding. Ignored
    /// without <see cref="Material"/> or by surfaces that declare no mask slot.
    /// </summary>
    public Texture2D? MaskTexture
    {
        get => _maskTexture;
        set
        {
            if (_maskTexture == value)
            {
                return;
            }
            _maskTexture = value;
        }
    }

    /// <summary>
    /// The mesh mode used to draw the image.
    /// </summary>
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

    /// <summary>
    /// Creates an image node with a simple quad mesh.
    /// </summary>
    public UIImage()
    {
    }

    /// <summary>
    /// Gets this node's material instance, creating it with the canvas camera and
    /// stencil state when needed and synchronizing the node's texture overrides.
    /// Other overrides do not modify the shared base material and are preserved
    /// while the instance is reused.
    /// </summary>
    /// <param name="canvas">The canvas that will draw the image.</param>
    /// <returns>The instance used for drawing, valid until the material or canvas
    /// changes, the node detaches, or the instance is disposed.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Material"/> is null.</exception>
    public GraphicsMaterialInstance GetMaterialInstance(Canvas canvas)
    {
        if (_material == null)
        {
            throw new InvalidOperationException($"UIImage '{Name}': Material must be set before requesting a material instance.");
        }

        if (_materialInstance == null || _materialInstance.IsDisposed || !ReferenceEquals(_materialCanvas, canvas))
        {
            DisposeMaterialInstance();
            _materialInstance = canvas.CreateUIMaterialInstance(canvas.GetSpriteMaterial(_material, BlendState.AlphaBlend));
            _materialCanvas = canvas;
        }

        if (!ReferenceEquals(_boundMaterialTexture, Texture))
        {
            _materialInstance.TrySetTexture(ShaderResourceId.Texture, Texture);
            _boundMaterialTexture = Texture;
        }
        if (!ReferenceEquals(_boundMaterialMaskTexture, MaskTexture))
        {
            _materialInstance.TrySetTexture(ShaderResourceId.Mask, MaskTexture);
            _boundMaterialMaskTexture = MaskTexture;
        }

        return _materialInstance;
    }

    private void DisposeMaterialInstance()
    {
        _materialInstance?.Dispose();
        _materialInstance = null;
        _materialCanvas = null;
        _boundMaterialTexture = null;
        _boundMaterialMaskTexture = null;
    }

    protected override void OnDetachFromTree(Canvas canvas)
    {
        DisposeMaterialInstance();
        base.OnDetachFromTree(canvas);
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

    /// <summary>
    /// Sets the image texture and UV rectangle from a sprite.
    /// </summary>
    /// <param name="sprite">The sprite whose texture and UV rectangle to use.</param>
    public void SetSprite(Sprite sprite)
    {
        Texture = sprite.Texture;
        UvRect = sprite.UvRect;
    }

    protected override void OnUpdateRenderData(Canvas canvas, float delta)
    {
        if (ImageType == ImageType.Sliced && Texture != null)
        {
            _lastMeshSize = Size;
            Vertices.SetSizeWithoutCopy(16);
            Indices.SetSizeWithoutCopy(54);
            MeshUtility.Populate9SliceMeshData(Vertices.AsSpan(0, 16), Indices.AsSpan(0, 54), new Vector2(Texture.Width, Texture.Height), _lastMeshSize, Texture.SlicePadding);
        }
        else if (ImageType == ImageType.Tiled && Texture != null)
        {
            _lastMeshSize = Size;
            Vertices.SetSizeWithoutCopy(4);
            Indices.SetSizeWithoutCopy(6);
            MeshUtility.PopulateTiledMeshData(Vertices.AsSpan(0, 4), Indices.AsSpan(0, 6), new Vector2(Texture.Width, Texture.Height), _lastMeshSize, TilingScale);
        }
    }


    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);

        if (_material != null)
        {
            DrawWithMaterial(canvas, delta);
            return;
        }

        if (ImageType == ImageType.Sliced && Texture != null)
        {
            // Detect implicit size changes (e.g., stretch anchor with parent resize)
            if (Size != _lastMeshSize)
            {
                OnUpdateRenderData(canvas, delta);
            }
            Transform2D transform = RenderTransform;
            transform.Scale = WorldTransform.Scale; // mesh already contains Size, keep only world scale
            canvas.DrawSpriteWithCustomMesh(Vertices.AsSpan(0, 16), Indices.AsSpan(0, 54), Texture, transform.Matrix, UvRect, RenderColor);
            return;
        }
        else if (ImageType == ImageType.Tiled && Texture != null)
        {
            // Detect implicit size changes (e.g., stretch anchor with parent resize)
            if (Size != _lastMeshSize)
            {
                OnUpdateRenderData(canvas, delta);
            }
            Transform2D transform = RenderTransform;
            transform.Scale = WorldTransform.Scale; // mesh already contains Size, keep only world scale
            canvas.DrawSpriteWithCustomMesh(Vertices.AsSpan(0, 4), Indices.AsSpan(0, 6), Texture, transform.Matrix, UvRect, RenderColor);
            return;
        }
        canvas.DrawSprite(Texture, RenderTransform.Matrix, UvRect, RenderColor);

    }

    /// <summary>
    /// Draws the image through its custom material: a centered quad for the simple
    /// type, the sliced/tiled mesh otherwise. Node textures are pushed into the
    /// material instance's texture/mask slots when the surface declares them.
    /// </summary>
    private void DrawWithMaterial(Canvas canvas, float delta)
    {
        GraphicsMaterialInstance instance = GetMaterialInstance(canvas);

        bool sliced = ImageType == ImageType.Sliced && Texture != null;
        bool tiled = ImageType == ImageType.Tiled && Texture != null;
        if (!sliced && !tiled)
        {
            SpriteConstant constant = new SpriteConstant
            {
                Model = RenderTransform.Matrix,
                Color = RenderColor,
                UvRect = UvRect
            };
            canvas.DrawMaterial(instance, in constant);
            return;
        }

        // Detect implicit size changes (e.g., stretch anchor with parent resize)
        if (Size != _lastMeshSize)
        {
            OnUpdateRenderData(canvas, delta);
        }
        Transform2D transform = RenderTransform;
        transform.Scale = WorldTransform.Scale; // mesh already contains Size, keep only world scale
        SpriteConstant meshConstant = new SpriteConstant
        {
            Model = transform.Matrix,
            Color = RenderColor,
            UvRect = UvRect
        };
        if (sliced)
        {
            canvas.DrawSpriteWithCustomMaterial(Vertices.AsSpan(0, 16), Indices.AsSpan(0, 54), instance, in meshConstant);
        }
        else
        {
            canvas.DrawSpriteWithCustomMaterial(Vertices.AsSpan(0, 4), Indices.AsSpan(0, 6), instance, in meshConstant);
        }
    }
}