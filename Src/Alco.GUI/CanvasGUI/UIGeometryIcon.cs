using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using NanoSvgSharp;

namespace Alco.GUI;

/// <summary>
/// UI node that renders an SVG document by flattening paths asynchronously on a worker thread
/// and scheduling bindless GPU compute rasterization through the owning <see cref="Canvas"/>.
/// </summary>
public class UIGeometryIcon : UINode
{
    /// <summary>The quantization step, in pixels, of rasterization target sizes.</summary>
    public const uint RasterSizeStep = 32;

    /// <summary>Ratio beyond which a stale SVG texture refreshes immediately without waiting for resize settle.</summary>
    public const float MaxStaleRatio = 1.5f;

    private string? _svg;
    private Texture? _texture;
    private Canvas? _attachedCanvas;
    private uint _rasterWidth;
    private uint _rasterHeight;
    private bool _svgChanged;
    private bool _invalidSvgLogged;

    private Task<SvgRasterizeData?>? _parseTask;
    private string? _parseTaskSvg;

    private Task<Vector2?>? _nativeSizeTask;
    private string? _nativeSizeSvg;

    private uint _settleWidth;
    private uint _settleHeight;
    private float _settleElapsed;

    /// <summary>
    /// The SVG document text to rasterize. Assigning a different document re-rasterizes on a following
    /// frame while the previous texture stays visible; assigning null or empty clears the icon.
    /// </summary>
    public string? Svg
    {
        get => _svg;
        set
        {
            if (value is not null && string.Equals(_svg, value, StringComparison.Ordinal))
                return;

            _svg = value;
            _svgChanged = true;
            _invalidSvgLogged = false;
            _settleElapsed = 0f;
            if (string.IsNullOrEmpty(value))
            {
                RetireCurrentTexture(_attachedCanvas);
                _rasterWidth = 0;
                _rasterHeight = 0;
            }
        }
    }

    /// <summary>Current rasterized premultiplied RGBA GPU texture, or null until geometry preparation completes.</summary>
    public Texture? Texture => _texture;

    /// <summary>Active background SVG parse and flatten task, if one is currently in flight.</summary>
    public Task? PendingParseTask => _parseTask;

    /// <summary>Seconds the node size must remain stable in a new quantized bucket before re-rasterizing.</summary>
    public float ResizeSettleSeconds { get; set; } = 0.1f;

    /// <summary>Optional custom <c>AlcoGUI_Pass</c> surface material shading the rasterized SVG texture.</summary>
    public Material? Material { get; set; }

    /// <summary>Sets <see cref="UINode.Size"/> to the SVG document's intrinsic size asynchronously.</summary>
    public void SetNativeSize()
    {
        if (string.IsNullOrEmpty(_svg))
            return;

        string svg = _svg;
        _nativeSizeSvg = svg;
        _nativeSizeTask = Task.Run<Vector2?>(() =>
        {
            if (!NanoSvg.TryParse(svg, "px", 96f, out NanoSvgImage? image) || image == null)
                return null;
            return image.Width > 0f && image.Height > 0f ? new Vector2(image.Width, image.Height) : null;
        });
    }

    protected override void OnAttachToTree(Canvas canvas)
    {
        _attachedCanvas = canvas;
        base.OnAttachToTree(canvas);
    }

    protected override void OnDetachFromTree(Canvas canvas)
    {
        RetireCurrentTexture(canvas);
        _attachedCanvas = null;
        base.OnDetachFromTree(canvas);
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);
        _attachedCanvas = canvas;

        PollParseTask(canvas);
        PollNativeSizeTask();

        if (!string.IsNullOrEmpty(_svg))
            UpdateRasterization(delta);

        if (_texture != null)
        {
            canvas.DrawSprite(_texture, RenderTransform.Matrix, Rect.One, RenderColor,
                premultiplied: true, nearestFilter: false, material: Material);
        }
    }

    private void RetireCurrentTexture(Canvas? canvas)
    {
        Texture? previous = _texture;
        _texture = null;
        if (previous == null)
            return;

        if (canvas != null)
            canvas.RetireTexture(previous);
        else if (!previous.IsDisposed)
            previous.Dispose();
    }

    private void PollParseTask(Canvas canvas)
    {
        Task<SvgRasterizeData?>? task = _parseTask;
        if (task == null || !task.IsCompleted)
            return;

        _parseTask = null;

        if (task.IsFaulted)
        {
            Log.Error($"UIGeometryIcon '{Name}': SVG geometry preparation failed.", task.Exception);
            return;
        }

        SvgRasterizeData? data = task.Result;
        if (data == null)
        {
            if (!_invalidSvgLogged)
            {
                _invalidSvgLogged = true;
                Log.Warning($"UIGeometryIcon '{Name}': Svg is not a valid SVG document.");
            }
            return;
        }

        if (ReferenceEquals(_parseTaskSvg, _svg))
        {
            RetireCurrentTexture(canvas);
            _texture = canvas.EnqueueSvgRasterize(in data);
            _rasterWidth = data.Width;
            _rasterHeight = data.Height;
        }
    }

    private void PollNativeSizeTask()
    {
        Task<Vector2?>? task = _nativeSizeTask;
        if (task == null || !task.IsCompleted)
            return;

        _nativeSizeTask = null;
        string? svgForTask = _nativeSizeSvg;
        _nativeSizeSvg = null;

        if (task.IsCompletedSuccessfully && ReferenceEquals(svgForTask, _svg) && task.Result is Vector2 size)
            Size = size;
    }

    private void UpdateRasterization(float delta)
    {
        if (_parseTask != null)
            return;

        (uint width, uint height) = ComputeRasterSize();
        if (width == 0 || height == 0)
            return;

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
                return;
        }

        StartParseAndFlatten(width, height);
    }

    private void StartParseAndFlatten(uint width, uint height)
    {
        string svg = _svg!;
        _parseTaskSvg = svg;
        _settleElapsed = 0f;
        _parseTask = Task.Run(() => SvgRenderer.TryParseAndBuild(svg, width, height, out SvgRasterizeData data) ? data : null);
    }

    private bool IsBeyondStale(uint width, uint height)
    {
        float ratio = MaxStaleRatio;
        return width > _rasterWidth * ratio
            || height > _rasterHeight * ratio
            || width * ratio < _rasterWidth
            || height * ratio < _rasterHeight;
    }

    private (uint Width, uint Height) ComputeRasterSize()
    {
        Vector2 renderSize = RenderSize;
        return (QuantizeSize(renderSize.X), QuantizeSize(renderSize.Y));
    }

    private static uint QuantizeSize(float pixels)
    {
        float magnitude = MathF.Min(MathF.Abs(pixels), SvgRenderer.MaxTextureSize);
        if (magnitude <= 0f)
            return 0;
        uint stepped = (uint)MathF.Ceiling(magnitude / RasterSizeStep) * RasterSizeStep;
        return Math.Clamp(stepped, RasterSizeStep, SvgRenderer.MaxTextureSize);
    }
}
