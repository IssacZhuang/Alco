using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using NanoSvgSharp;

namespace Alco.GUI;

/// <summary>
/// A UI node that renders an SVG document rasterized by the canvas's shared
/// <see cref="SvgRenderer"/>. Rasterization runs on background threads and its
/// result is adopted on a later frame, so assigning <see cref="Svg"/> or resizing
/// the node never stalls the UI thread.
/// </summary>
/// <remarks>
/// The latest completed texture is drawn stretched while sizes change; textures come from a shared weak cache and are never disposed by this node.
/// </remarks>
public class UIGeometryIcon : UINode
{
    /// <summary>The quantization step, in pixels, of rasterization target sizes.</summary>
    public const uint RasterSizeStep = 32;

    /// <summary>
    /// The on-screen size ratio beyond which a stale rasterization is refreshed
    /// immediately instead of waiting for the resize to settle.
    /// </summary>
    public const float MaxStaleRatio = 1.5f;

    private string? _svg;
    private Texture2D? _texture;
    private uint _rasterWidth;
    private uint _rasterHeight;
    private bool _svgChanged;
    private bool _invalidSvgLogged;
    private MaterialAsset? _material;
    private GraphicsMaterialInstance? _materialInstance;
    private Canvas? _materialCanvas;
    private Texture2D? _boundMaterialTexture;

    private Task<Texture2D?>? _rasterTask;
    private string? _rasterTaskSvg;

    private Task<Vector2?>? _nativeSizeTask;
    private string? _nativeSizeSvg;

    private uint _settleWidth;
    private uint _settleHeight;
    private float _settleElapsed;

    /// <summary>
    /// The SVG document text to rasterize. Assigning a different document
    /// re-rasterizes on a following frame while the previous texture stays
    /// visible; assigning null or an empty string clears the icon.
    /// </summary>
    public string? Svg
    {
        get => _svg;
        set
        {
            if (value is not null && string.Equals(_svg, value, StringComparison.Ordinal))
            {
                return;
            }
            _svg = value;
            _svgChanged = true;
            _invalidSvgLogged = false;
            _settleElapsed = 0f;
            if (string.IsNullOrEmpty(value))
            {
                _texture = null;
                _rasterWidth = 0;
                _rasterHeight = 0;
            }
        }
    }

    /// <summary>
    /// The current rasterized texture, or null before the first rasterization
    /// completes (or after assigning an empty <see cref="Svg"/>). May briefly lag
    /// behind the node size while a resize settles.
    /// </summary>
    public Texture2D? Texture => _texture;

    /// <summary>
    /// How long, in seconds, the node size must stay in a new rasterization bucket
    /// before a re-rasterization is issued. Zero re-rasterizes as soon as the
    /// bucket changes.
    /// </summary>
    public float ResizeSettleSeconds { get; set; } = 0.1f;

    /// <summary>
    /// The material asset whose surface draws the icon instead of the premultiplied
    /// sprite material. The surface must implement the engine's <c>IUISurface</c>
    /// protocol and return premultiplied RGBA for premultiplied-alpha blending.
    /// The rasterized texture is bound to the surface's texture slot when declared.
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
    /// Gets or creates this node's material instance for the given canvas, binding the current rasterized texture.
    /// </summary>
    /// <param name="canvas">The canvas that will draw the icon.</param>
    /// <returns>The instance used for drawing, valid until the material or canvas changes or the node detaches.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Material"/> is null.</exception>
    public GraphicsMaterialInstance GetMaterialInstance(Canvas canvas)
    {
        if (_material == null)
        {
            throw new InvalidOperationException($"UIGeometryIcon '{Name}': Material must be set before requesting a material instance.");
        }

        if (_materialInstance == null || _materialInstance.IsDisposed || !ReferenceEquals(_materialCanvas, canvas))
        {
            DisposeMaterialInstance();
            _materialInstance = canvas.CreateUIMaterialInstance(canvas.GetSpriteMaterial(_material, BlendState.PremultipliedAlpha));
            _materialCanvas = canvas;
        }

        if (!ReferenceEquals(_boundMaterialTexture, _texture))
        {
            _materialInstance.TrySetTexture(ShaderResourceId.Texture, _texture);
            _boundMaterialTexture = _texture;
        }

        return _materialInstance;
    }

    private void DisposeMaterialInstance()
    {
        _materialInstance?.Dispose();
        _materialInstance = null;
        _materialCanvas = null;
        _boundMaterialTexture = null;
    }

    protected override void OnDetachFromTree(Canvas canvas)
    {
        DisposeMaterialInstance();
        base.OnDetachFromTree(canvas);
    }

    /// <summary>
    /// Sets <see cref="UINode.Size"/> to the SVG document's intrinsic size, parsed asynchronously; does nothing when the document is empty, invalid, or unsized.
    /// </summary>
    public void SetNativeSize()
    {
        if (string.IsNullOrEmpty(_svg))
        {
            return;
        }
        string svg = _svg;
        _nativeSizeSvg = svg;
        _nativeSizeTask = Task.Run<Vector2?>(() =>
        {
            if (!NanoSvg.TryParse(svg, out NanoSvgImage? image) || image == null)
            {
                return null;
            }
            return image.Width > 0f && image.Height > 0f ? new Vector2(image.Width, image.Height) : null;
        });
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);

        PollRasterTask();
        PollNativeSizeTask();

        if (!string.IsNullOrEmpty(_svg))
        {
            UpdateRasterization(canvas, delta);
        }

        if (_texture != null)
        {
            if (_material == null)
            {
                canvas.DrawSpritePremultiplied(_texture, RenderTransform.Matrix, Rect.One, RenderColor);
                return;
            }

            GraphicsMaterialInstance instance = GetMaterialInstance(canvas);

            SpriteConstant constant = new SpriteConstant
            {
                Model = RenderTransform.Matrix,
                Color = RenderColor,
                UvRect = Rect.One
            };
            canvas.DrawMaterial(instance, in constant);
        }
    }

    /// <summary>Adopts the completed rasterization when it belongs to the current
    /// document; superseded or failed results are dropped.</summary>
    private void PollRasterTask()
    {
        Task<Texture2D?>? task = _rasterTask;
        if (task == null || !task.IsCompleted)
        {
            return;
        }
        _rasterTask = null;

        if (task.IsFaulted)
        {
            Log.Error($"UIGeometryIcon '{Name}': rasterization failed.", task.Exception);
            return;
        }

        Texture2D? texture = task.Result;
        if (texture == null)
        {
            if (!_invalidSvgLogged)
            {
                _invalidSvgLogged = true;
                Log.Warning($"UIGeometryIcon '{Name}': Svg is not a valid SVG document.");
            }
            return;
        }

        if (ReferenceEquals(_rasterTaskSvg, _svg))
        {
            _texture = texture;
            _rasterWidth = texture.Width;
            _rasterHeight = texture.Height;
        }
    }

    /// <summary>Applies the completed intrinsic-size parse when it belongs to the
    /// current document.</summary>
    private void PollNativeSizeTask()
    {
        Task<Vector2?>? task = _nativeSizeTask;
        if (task == null || !task.IsCompleted)
        {
            return;
        }
        _nativeSizeTask = null;
        _nativeSizeSvg = null;

        if (task.IsCompletedSuccessfully && task.Result is Vector2 size)
        {
            Size = size;
        }
    }

    /// <summary>Decides whether a rasterization is due for the current document
    /// and on-screen size, honoring the settle debounce and the staleness bound,
    /// and kicks it on a background thread.</summary>
    private void UpdateRasterization(Canvas canvas, float delta)
    {
        if (_rasterTask != null)
        {
            // One rasterization in flight at a time; the desired size is recomputed
            // from the live state after it lands.
            return;
        }

        (uint width, uint height) = ComputeRasterSize();
        if (width == 0 || height == 0)
        {
            // Not laid out yet (or scaled to nothing).
            return;
        }

        bool immediate = _svgChanged || _texture == null;
        _svgChanged = false;

        if (!immediate && width == _rasterWidth && height == _rasterHeight)
        {
            _settleElapsed = 0f;
            return;
        }

        if (!immediate && !IsBeyondStale(width, height))
        {
            if (width != _settleWidth || height != _settleHeight)
            {
                _settleWidth = width;
                _settleHeight = height;
                _settleElapsed = 0f;
            }
            _settleElapsed += delta;
            if (_settleElapsed < MathF.Max(0f, ResizeSettleSeconds))
            {
                return;
            }
        }

        StartRasterization(canvas, width, height);
    }

    private void StartRasterization(Canvas canvas, uint width, uint height)
    {
        string svg = _svg!;
        _rasterTaskSvg = svg;
        _settleElapsed = 0f;
        SvgRenderer renderer = canvas.SvgRenderer;
        _rasterTask = Task.Run(() => renderer.TryGetTexture(svg, width, height, out Texture2D? texture) ? texture : null);
    }

    /// <summary>Returns true when the current texture is blurry enough (or wasted
    /// enough) at the desired size that waiting for the resize to settle is worse
    /// than re-rasterizing immediately.</summary>
    private bool IsBeyondStale(uint width, uint height)
    {
        float ratio = MaxStaleRatio;
        return width > _rasterWidth * ratio
            || height > _rasterHeight * ratio
            || width * ratio < _rasterWidth
            || height * ratio < _rasterHeight;
    }

    /// <summary>Quantizes the on-screen size (including parent scale) up to a
    /// <see cref="RasterSizeStep"/> bucket clamped to the renderer's size range;
    /// 0 when the node is scaled to nothing.</summary>
    private (uint Width, uint Height) ComputeRasterSize()
    {
        Vector2 renderSize = RenderSize;
        return (QuantizeSize(renderSize.X), QuantizeSize(renderSize.Y));
    }

    private static uint QuantizeSize(float pixels)
    {
        float magnitude = MathF.Min(MathF.Abs(pixels), SvgRenderer.MaxTextureSize);
        if (magnitude <= 0f)
        {
            return 0;
        }
        uint stepped = (uint)MathF.Ceiling(magnitude / RasterSizeStep) * RasterSizeStep;
        return Math.Clamp(stepped, RasterSizeStep, SvgRenderer.MaxTextureSize);
    }
}
