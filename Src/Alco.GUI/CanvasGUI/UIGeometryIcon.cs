using System.Numerics;
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
/// <para>
/// High-frequency resizes (stretch anchors, size animations, window resizing) are
/// handled by decoupling drawing from rasterization: the latest completed texture
/// is always drawn stretched across the node rect, so the icon never disappears
/// while sizes change. Rasterization targets are quantized to
/// <see cref="RasterSizeStep"/>-pixel buckets of the on-screen size, a resize must
/// stay in a new bucket for <see cref="ResizeSettleSeconds"/> before a
/// re-rasterization is issued, and an on-screen size diverging from the raster
/// size beyond <see cref="MaxStaleRatio"/> re-rasterizes immediately to bound
/// blurriness. At most one rasterization per node is in flight; superseded
/// results are dropped and the corrected request follows on the next frame.
/// </para>
/// <para>
/// The textures come from the renderer's weak cache and may be shared with other
/// nodes, so this node never disposes them; dropping the reference is enough.
/// </para>
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
    /// Sets <see cref="UINode.Size"/> to the document's intrinsic size (the
    /// width/height attributes, or the viewBox size when they are absent). The
    /// document is parsed on a background thread and the size is applied on a
    /// later frame once known; the result is dropped if <see cref="Svg"/> changes
    /// in between. Does nothing when the document is empty, invalid or unsized.
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
            canvas.DrawSpritePremultiplied(_texture, RenderTransform.Matrix, Rect.One, RenderColor);
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
