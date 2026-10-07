using System.Numerics;
using Alco.Editor.Extensibility;
using Alco.ImGUI;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// The 2D <see cref="PreviewViewport"/>: an orthographic top-down camera that
/// pans (left drag, content follows the mouse) and zooms (wheel) around a view
/// center, with a default view a few world units wide. The grid and axes cover
/// the visible world rectangle.
/// </summary>
public sealed class PreviewViewport2D : PreviewViewport
{
    private readonly float _baseViewWidth;
    private readonly Camera2DBuffer _camera;
    private float _zoom = 1f;

    /// <summary>Creates the viewport with its 2D camera.</summary>
    /// <param name="context">The editor context (engine services).</param>
    /// <param name="name">The base name for the pipeline and its resources.</param>
    /// <param name="baseViewWidth">The view width in world units at zoom 1.</param>
    /// <param name="pipelineFactory">The pipeline factory; null uses the default preview pipeline.</param>
    public PreviewViewport2D(EditorContext context, string name, float baseViewWidth = 4f, IPreviewPipelineFactory? pipelineFactory = null)
        : base(context, name, pipelineFactory)
    {
        _baseViewWidth = baseViewWidth;
        _camera = context.RenderingSystem.CreateCamera2D(baseViewWidth, baseViewWidth * 9f / 16f, 100f, name + "_2d");
    }

    /// <summary>The 2D camera, for systems that render into the scene pass.</summary>
    public Camera2DBuffer Camera => _camera;

    /// <summary>The camera state: the zoom multiplier over the base view width and the view center.</summary>
    public (float Zoom, Vector2 Position) CameraState => (_zoom, _camera.Position);

    /// <summary>Sets the camera zoom and center, applying the viewport's zoom clamps.</summary>
    /// <param name="zoom">The zoom multiplier over the base view width (smaller zooms in).</param>
    /// <param name="position">The world-space view center.</param>
    public void SetCameraState(float zoom, Vector2 position)
    {
        _zoom = Math.Clamp(zoom, 0.05f, 40f);
        _camera.Position = position;
        UpdateCamera();
    }

    /// <inheritdoc />
    protected override Matrix4x4 ViewProjection => _camera.Data.ViewProjectionMatrix;

    /// <inheritdoc />
    protected override float PixelsPerUnit => (float)Target.Width / _camera.ViewSize.X;

    /// <inheritdoc />
    protected override string CameraStatus => $"zoom {_zoom:0.##}x";

    /// <inheritdoc />
    protected override string InputHint => "2D — left-drag pans, wheel zooms";

    /// <inheritdoc />
    protected override void CommitCamera()
    {
        _camera.UpdateMatrixToGPU();
    }

    /// <inheritdoc />
    public override void ResetCamera()
    {
        _zoom = 1f;
        _camera.Position = Vector2.Zero;
        UpdateCamera();
    }

    /// <inheritdoc />
    protected override void OnTargetResize(uint pixelWidth, uint pixelHeight)
    {
        UpdateCamera();
    }

    /// <inheritdoc />
    protected override void OnWheel(float wheel)
    {
        _zoom = Math.Clamp(_zoom * MathF.Pow(0.9f, wheel), 0.05f, 40f);
        UpdateCamera();
    }

    /// <inheritdoc />
    protected override void OnLeftDrag(Vector2 delta, Vector2 imageSize)
    {
        // Grab-style pan: the content follows the mouse. World Y points up but
        // screen Y points down, so the vertical component is flipped.
        Vector2 worldPerPixel = _camera.ViewSize / imageSize;
        _camera.Position -= new Vector2(delta.X, -delta.Y) * worldPerPixel;
    }

    /// <summary>Applies the zoom to the camera, keeping the base view width.</summary>
    private void UpdateCamera()
    {
        float aspect = Target.Height == 0 ? 16f / 9f : (float)Target.Width / Target.Height;
        float width = _baseViewWidth * _zoom;
        _camera.ViewSize = new Vector2(width, width / aspect);
    }

    /// <inheritdoc />
    protected override void DrawGridLines(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float step)
    {
        uint color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f));
        // Cover the visible world rectangle.
        Vector2 center = _camera.Position;
        Vector2 half = _camera.ViewSize * 0.5f;
        for (float x = MathF.Floor((center.X - half.X) / step) * step; x <= center.X + half.X; x += step)
        {
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                new Vector3(x, center.Y - half.Y, 0f), new Vector3(x, center.Y + half.Y, 0f), color);
        }
        for (float y = MathF.Floor((center.Y - half.Y) / step) * step; y <= center.Y + half.Y; y += step)
        {
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                new Vector3(center.X - half.X, y, 0f), new Vector3(center.X + half.X, y, 0f), color);
        }
    }

    /// <inheritdoc />
    protected override void DrawAxisLines(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float step)
    {
        uint xColor = ImGui.GetColorU32(new Vector4(1f, 0.3f, 0.3f, 0.8f));
        uint yColor = ImGui.GetColorU32(new Vector4(0.35f, 0.9f, 0.35f, 0.8f));
        const float thickness = 1.5f;
        Vector2 center = _camera.Position;
        Vector2 half = _camera.ViewSize * 0.5f;
        DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
            new Vector3(center.X - half.X, 0f, 0f), new Vector3(center.X + half.X, 0f, 0f), xColor, thickness);
        DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
            new Vector3(0f, center.Y - half.Y, 0f), new Vector3(0f, center.Y + half.Y, 0f), yColor, thickness);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _camera.Dispose();
        }
        base.Dispose(disposing);
    }
}
