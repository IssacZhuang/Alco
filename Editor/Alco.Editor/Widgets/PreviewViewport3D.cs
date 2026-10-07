using System.Numerics;
using Alco.Editor.Extensibility;
using Alco.ImGUI;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// The 3D <see cref="PreviewViewport"/>: a perspective camera on an orbit around
/// a target — left drag orbits, middle drag pans the target, wheel dollies.
/// The grid lies on the XY ground plane (Z+ up) around the origin.
/// </summary>
public sealed class PreviewViewport3D : PreviewViewport
{
    private readonly CameraPerspectiveBuffer _camera;
    private float _orbitYaw = MathF.PI;
    private float _orbitPitch = 0.26f;
    private float _orbitDistance = 8f;
    private Vector3 _orbitTarget = Vector3.Zero;

    /// <summary>Creates the viewport with its perspective camera.</summary>
    /// <param name="context">The editor context (engine services).</param>
    /// <param name="name">The base name for the pipeline and its resources.</param>
    /// <param name="pipelineFactory">The pipeline factory; null uses the default preview pipeline.</param>
    public PreviewViewport3D(EditorContext context, string name, IPreviewPipelineFactory? pipelineFactory = null)
        : base(context, name, pipelineFactory)
    {
        _camera = context.RenderingSystem.CreateCameraPerspective(0.9f, 16f / 9f, 0.1f, 300f, name + "_3d");
        UpdateCamera();
    }

    /// <summary>The perspective camera, for systems that render into the scene pass.</summary>
    public CameraPerspectiveBuffer Camera => _camera;

    /// <summary>The orbit camera state: yaw, pitch, distance and orbit target.</summary>
    public (float Yaw, float Pitch, float Distance, Vector3 Target) CameraState =>
        (_orbitYaw, _orbitPitch, _orbitDistance, _orbitTarget);

    /// <summary>Sets the orbit camera, applying the viewport's pitch/distance clamps.</summary>
    /// <param name="yaw">The orbit yaw in radians.</param>
    /// <param name="pitch">The orbit pitch in radians.</param>
    /// <param name="distance">The orbit distance.</param>
    /// <param name="target">The orbit target.</param>
    public void SetCameraState(float yaw, float pitch, float distance, Vector3 target)
    {
        _orbitYaw = yaw;
        _orbitPitch = Math.Clamp(pitch, -1.5f, 1.5f);
        _orbitDistance = Math.Clamp(distance, 1f, 200f);
        _orbitTarget = target;
        UpdateCamera();
    }

    /// <inheritdoc />
    protected override Matrix4x4 ViewProjection => _camera.Data.ViewProjectionMatrix;

    /// <inheritdoc />
    protected override float PixelsPerUnit => (float)Target.Height / (2f * _orbitDistance * MathF.Tan(_camera.FieldOfView * 0.5f));

    /// <inheritdoc />
    protected override string CameraStatus => $"dist {_orbitDistance:0.##}";

    /// <inheritdoc />
    protected override string InputHint => "3D — left-drag orbits, middle-drag pans, wheel zooms";

    /// <inheritdoc />
    protected override void CommitCamera()
    {
        // Mutating the camera through its Transform ref does not flag the buffer
        // dirty; upload explicitly (see the call site's comment).
        _camera.UpdateMatrixToGPU();
    }

    /// <inheritdoc />
    public override void ResetCamera()
    {
        _orbitYaw = MathF.PI;
        _orbitPitch = 0.26f;
        _orbitDistance = 8f;
        _orbitTarget = Vector3.Zero;
        UpdateCamera();
    }

    /// <inheritdoc />
    protected override void OnTargetResize(uint pixelWidth, uint pixelHeight)
    {
        _camera.AspectRatio = (float)pixelWidth / pixelHeight;
    }

    /// <inheritdoc />
    protected override void OnWheel(float wheel)
    {
        _orbitDistance = Math.Clamp(_orbitDistance * MathF.Pow(0.9f, wheel), 1f, 200f);
        UpdateCamera();
    }

    /// <inheritdoc />
    protected override void OnLeftDrag(Vector2 delta, Vector2 imageSize)
    {
        // Orbit: dragging right swings the camera to its left around the target
        // (the scene appears to rotate right), matching DCC tools.
        _orbitYaw += delta.X * 0.008f;
        _orbitPitch = Math.Clamp(_orbitPitch + delta.Y * 0.008f, -1.5f, 1.5f);
        UpdateCamera();
    }

    /// <inheritdoc />
    protected override void OnMiddleDrag(Vector2 delta, Vector2 imageSize)
    {
        // Pan: slide the orbit target in the camera's right/up plane so the
        // content follows the mouse, matching the 2D drag convention.
        float worldPerPixel = 2f * _orbitDistance * MathF.Tan(_camera.FieldOfView * 0.5f) / imageSize.Y;
        Vector3 position = GetOrbitPosition();
        (Vector3 right, Vector3 up, _) = ComputeCameraBasis(position, _orbitTarget);
        _orbitTarget += -right * (delta.X * worldPerPixel) + up * (delta.Y * worldPerPixel);
        UpdateCamera();
    }

    /// <summary>The camera position on its orbit around the orbit target.</summary>
    private Vector3 GetOrbitPosition()
    {
        return _orbitTarget + new Vector3(
            _orbitDistance * MathF.Cos(_orbitPitch) * MathF.Cos(_orbitYaw),
            _orbitDistance * MathF.Cos(_orbitPitch) * MathF.Sin(_orbitYaw),
            _orbitDistance * MathF.Sin(_orbitPitch));
    }

    /// <summary>
    /// Builds the camera basis for the engine convention: forward = +X, up = +Z
    /// (Transform3D.LookAt aims +Z instead, so the rotation is built manually).
    /// </summary>
    private static (Vector3 Right, Vector3 Up, Vector3 Forward) ComputeCameraBasis(Vector3 position, Vector3 target)
    {
        Vector3 forward = Vector3.Normalize(target - position);
        Vector3 up = Vector3.Normalize(Vector3.UnitZ - forward * Vector3.Dot(forward, Vector3.UnitZ));
        return (Vector3.Cross(up, forward), up, forward);
    }

    /// <summary>Repositions the camera on its orbit around the orbit target.</summary>
    private void UpdateCamera()
    {
        Vector3 position = GetOrbitPosition();
        (Vector3 right, Vector3 up, Vector3 forward) = ComputeCameraBasis(position, _orbitTarget);
        Matrix4x4 rotation = new(
            forward.X, forward.Y, forward.Z, 0,
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            0, 0, 0, 1);
        _camera.Transform.Position = position;
        _camera.Transform.Rotation = Quaternion.CreateFromRotationMatrix(rotation);
    }

    /// <inheritdoc />
    protected override void DrawGridLines(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float step)
    {
        uint color = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f));
        // Fixed extent around the origin on the ground plane (Z = 0).
        float extent = step * 10f;
        for (float f = -extent; f <= extent; f += step)
        {
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                new Vector3(-extent, f, 0f), new Vector3(extent, f, 0f), color);
            DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
                new Vector3(f, -extent, 0f), new Vector3(f, extent, 0f), color);
        }
    }

    /// <inheritdoc />
    protected override void DrawAxisLines(ImDrawListPtr drawList, in Matrix4x4 viewProjection, Vector2 imageMin, Vector2 imageSize, float step)
    {
        uint xColor = ImGui.GetColorU32(new Vector4(1f, 0.3f, 0.3f, 0.8f));
        uint yColor = ImGui.GetColorU32(new Vector4(0.35f, 0.9f, 0.35f, 0.8f));
        const float thickness = 1.5f;
        // Never shrink with a fine grid step, so the axes stay visible when zoomed in.
        float extent = MathF.Max(step * 10f, 10f);
        DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
            new Vector3(-extent, 0f, 0f), new Vector3(extent, 0f, 0f), xColor, thickness);
        DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
            new Vector3(0f, -extent, 0f), new Vector3(0f, extent, 0f), yColor, thickness);
        // Z+ up: only the positive half pokes out of the ground plane grid.
        uint zColor = ImGui.GetColorU32(new Vector4(0.35f, 0.5f, 1f, 0.8f));
        DrawWorldLine(drawList, viewProjection, imageMin, imageSize,
            Vector3.Zero, new Vector3(0f, 0f, extent), zColor, thickness);
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
