using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// Identifies a single gizmo handle (or none). Values are ordered so that
/// translate handles come first, then rotate, then scale, mirroring the ImGuizmo
/// MOVETYPE layout; arithmetic on the values is used to map handles to axes.
/// </summary>
internal enum GizmoMoveType
{
    /// <summary>No handle.</summary>
    None = 0,

    /// <summary>Translate along X.</summary>
    MoveX,

    /// <summary>Translate along Y.</summary>
    MoveY,

    /// <summary>Translate along Z.</summary>
    MoveZ,

    /// <summary>Translate in the YZ plane (normal X).</summary>
    MoveYZ,

    /// <summary>Translate in the ZX plane (normal Y).</summary>
    MoveZX,

    /// <summary>Translate in the XY plane (normal Z).</summary>
    MoveXY,

    /// <summary>Translate in screen space (center handle).</summary>
    MoveScreen,

    /// <summary>Rotate around X.</summary>
    RotateX,

    /// <summary>Rotate around Y.</summary>
    RotateY,

    /// <summary>Rotate around Z.</summary>
    RotateZ,

    /// <summary>Rotate around the camera view axis.</summary>
    RotateScreen,

    /// <summary>Scale along X.</summary>
    ScaleX,

    /// <summary>Scale along Y.</summary>
    ScaleY,

    /// <summary>Scale along Z.</summary>
    ScaleZ,

    /// <summary>Scale uniformly on all axes.</summary>
    ScaleXYZ,
}

/// <summary>
/// Holds all gizmo state across frames: persistent configuration, the input
/// snapshot, the active drag state, and the per-<see cref="GizmoCore.Manipulate"/>
/// call working set (matrices, camera ray, screen factor). At most one handle can
/// be active at a time; the active drag survives frame boundaries while the hover
/// state is recomputed every frame.
/// </summary>
internal sealed class GizmoContext
{
    // Persistent configuration.

    /// <summary>Screen rectangle the gizmo operates in, in pixels. Hit-testing is skipped outside of it.</summary>
    public Rect Viewport;

    /// <summary>Whether the camera is orthographic. Affects the behind-camera check and rotation ring orientation.</summary>
    public bool IsOrthographic;

    /// <summary>Visual style of the gizmo.</summary>
    public readonly GizmoStyle Style = new GizmoStyle();

    // Per-frame input.

    /// <summary>Current frame's input snapshot.</summary>
    public GizmoInput Input;

    /// <summary>Left mouse button state captured at the previous <see cref="BeginFrame"/>.</summary>
    public bool PreviousMouseDown;

    // Cross-frame interaction state.

    /// <summary>Whether a drag is currently active.</summary>
    public bool Using;

    /// <summary>The handle the active drag belongs to.</summary>
    public GizmoMoveType CurrentOperation;

    /// <summary>Aggregated hover/active handle of the current frame; reset by <see cref="BeginFrame"/>.</summary>
    public GizmoMoveType FrameHoverType;

    /// <summary>Set once any handle was hit this frame; prevents multiple gizmos from highlighting in the same frame.</summary>
    public bool OverGizmoHotspot;

    /// <summary>
    /// Display-only multiplier applied to the translation drag info text, for editors whose
    /// authoring unit differs from world units (e.g. texels). Reset to 1 by <see cref="BeginFrame"/>.
    /// </summary>
    public float InfoUnitScale = 1f;

    // Per-call working set, recomputed by every Manipulate call and consumed by the draw layer.

    /// <summary>Whether the last Manipulate call produced a drawable/interactive gizmo (false for degenerate matrices or behind-camera origins).</summary>
    public bool CallValid;

    /// <summary>Mode the current call solves in (scale operations are forced to <see cref="GizmoMode.Local"/>).</summary>
    public GizmoMode Mode;

    /// <summary>Operation flags of the current call.</summary>
    public GizmoOperation Operation;

    /// <summary>Hover/active handle of the current call, used by the draw layer for highlighting.</summary>
    public GizmoMoveType CallType;

    /// <summary>View matrix of the current call.</summary>
    public Matrix4x4 ViewMatrix;

    /// <summary>Projection matrix of the current call.</summary>
    public Matrix4x4 ProjectionMatrix;

    /// <summary>Display model matrix: the orthonormalized model in Local mode, translation-only in World mode.</summary>
    public Matrix4x4 Model;

    /// <summary>Orthonormalized source model matrix (rotation without scale).</summary>
    public Matrix4x4 ModelLocal;

    /// <summary>Inverse of <see cref="Model"/>.</summary>
    public Matrix4x4 ModelInverse;

    /// <summary>Unmodified source model matrix.</summary>
    public Matrix4x4 ModelSource;

    /// <summary>Inverse of <see cref="ModelSource"/>.</summary>
    public Matrix4x4 ModelSourceInverse;

    /// <summary><see cref="Model"/> * view * projection.</summary>
    public Matrix4x4 Mvp;

    /// <summary><see cref="ModelLocal"/> * view * projection.</summary>
    public Matrix4x4 MvpLocal;

    /// <summary>View * projection.</summary>
    public Matrix4x4 ViewProjection;

    /// <summary>Scale extracted from the source matrix (row lengths).</summary>
    public Vector3 ModelScaleOrigin;

    /// <summary>Camera position in world space.</summary>
    public Vector3 CameraEye;

    /// <summary>Camera right axis in world space.</summary>
    public Vector3 CameraRight;

    /// <summary>Camera forward axis in world space.</summary>
    public Vector3 CameraDir;

    /// <summary>Camera up axis in world space.</summary>
    public Vector3 CameraUp;

    /// <summary>World-space ray origin under the mouse cursor.</summary>
    public Vector3 RayOrigin;

    /// <summary>World-space ray direction under the mouse cursor.</summary>
    public Vector3 RayVector;

    /// <summary>World-space scale factor that keeps handles at a constant screen size.</summary>
    public float ScreenFactor;

    /// <summary>Radius in pixels of the screen-space rotation ring; maintained by the draw layer and read by hit-testing.</summary>
    public float RadiusSquareCenter;

    /// <summary>Screen position of the gizmo center.</summary>
    public Vector2 ScreenSquareCenter;

    /// <summary>Top-left of the center handle hit area.</summary>
    public Vector2 ScreenSquareMin;

    /// <summary>Bottom-right of the center handle hit area.</summary>
    public Vector2 ScreenSquareMax;

    /// <summary>Whether the projection uses reversed depth.</summary>
    public bool Reversed;

    // Drag state, captured at activation and kept for the duration of the drag.

    /// <summary>Translation/rotation solve plane (xyz = normal, w = offset).</summary>
    public Vector4 TranslationPlan;

    /// <summary>Ray hit point on the solve plane at drag start.</summary>
    public Vector3 TranslationPlanOrigin;

    /// <summary>Model position at drag start.</summary>
    public Vector3 MatrixOrigin;

    /// <summary>Translation delta applied by the previous solve, used for change detection.</summary>
    public Vector3 TranslationLastDelta;

    /// <summary>Grab point direction at rotation drag start.</summary>
    public Vector3 RotationVectorSource;

    /// <summary>Current cumulative rotation angle in radians.</summary>
    public float RotationAngle;

    /// <summary>Rotation angle of the previous solve, used for change detection.</summary>
    public float RotationAngleOrigin;

    /// <summary>Current cumulative scale multiplier.</summary>
    public Vector3 Scale;

    /// <summary>Object scale at drag start.</summary>
    public Vector3 ScaleValueOrigin;

    /// <summary>Scale multiplier of the previous solve, used for change detection.</summary>
    public Vector3 ScaleLast;

    /// <summary>Mouse X position at uniform-scale drag start.</summary>
    public float SaveMousePosX;

    /// <summary>Grab offset between the gizmo origin and the plane hit point, in gizmo-sized units.</summary>
    public Vector3 RelativeOrigin;

    // Axis visibility state, frozen while dragging so handles do not flip mid-drag.

    /// <summary>Per-axis sign flip that keeps handles pointing toward the camera.</summary>
    public readonly float[] AxisFactor = new float[3];

    /// <summary>Per-axis visibility flag for axis handles.</summary>
    public readonly bool[] BelowAxisLimit = new bool[3];

    /// <summary>Per-axis visibility flag for plane handles.</summary>
    public readonly bool[] BelowPlaneLimit = new bool[3];

    /// <summary>
    /// Starts a new frame: stores the input snapshot, sets the viewport, resets the
    /// per-frame hover state and keeps the active drag state.
    /// </summary>
    /// <param name="viewport">Screen rectangle the gizmo operates in.</param>
    /// <param name="input">Input snapshot for the frame.</param>
    public void BeginFrame(Rect viewport, in GizmoInput input)
    {
        PreviousMouseDown = Input.MouseDown;
        Input = input;
        Viewport = viewport;
        FrameHoverType = GizmoMoveType.None;
        OverGizmoHotspot = false;
        InfoUnitScale = 1f;
    }
}
