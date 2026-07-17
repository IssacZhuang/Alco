using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// Immediate-mode manipulation gizmo (translate/rotate/scale handles), a pure C#
/// replacement for ImGuizmo built on the engine's math conventions: row-major
/// left-handed matrices, Z+ up, 2D X+ right / Y+ up. All overloads draw into the
/// current ImGui window's draw list and therefore must be called inside an
/// ImGui window scope (<see cref="ImGui.Begin(string)"/> / <see cref="ImGui.End"/>).
/// </summary>
public static class Gizmo
{
    private static readonly GizmoContext _context = new GizmoContext();

    /// <summary>
    /// Whether a gizmo handle is currently being dragged. Editors use this to
    /// suppress other mouse logic (e.g. camera control) while manipulating.
    /// </summary>
    public static bool IsUsing => _context.Using;

    /// <summary>Whether the mouse is over any gizmo handle, or a drag is active.</summary>
    public static bool IsOver => _context.FrameHoverType != GizmoMoveType.None || _context.Using;

    /// <summary>
    /// Whether the camera is orthographic. Persistent property replacing the old
    /// per-frame SetOrthographic call; affects the behind-camera check and the
    /// rotation ring orientation.
    /// </summary>
    public static bool IsOrthographic
    {
        get => _context.IsOrthographic;
        set => _context.IsOrthographic = value;
    }

    /// <summary>
    /// Screen rectangle the gizmo operates in, in pixels. Reset to the full render
    /// target by <see cref="BeginFrame"/> every frame; set it after BeginFrame to
    /// restrict the gizmo to a sub-region. Mouse outside the viewport never hits.
    /// </summary>
    public static Rect Viewport
    {
        get => _context.Viewport;
        set => _context.Viewport = value;
    }

    /// <summary>Visual style of the gizmo (colors, handle sizes, line widths).</summary>
    public static GizmoStyle Style => _context.Style;

    /// <summary>
    /// Per-frame reset driven by the renderer: snapshots the mouse input from ImGui,
    /// sets the viewport to the full render target and clears the hover state while
    /// keeping any active drag. Called by <see cref="ImGUIRenderer.Begin"/>.
    /// </summary>
    /// <param name="width">Render target width in pixels.</param>
    /// <param name="height">Render target height in pixels.</param>
    internal static void BeginFrame(float width, float height)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        GizmoInput input = new GizmoInput(
            io.MousePos,
            ImGui.IsMouseDown(ImGuiMouseButton.Left),
            io.MouseDelta,
            !ImGui.IsAnyItemHovered() && !ImGui.IsAnyItemActive());
        _context.BeginFrame(new Rect(0f, 0f, width, height), in input);
    }

    /// <summary>
    /// Manipulates a 3D transform with the gizmo and draws the handles.
    /// </summary>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="operation">The handles to display and respond to.</param>
    /// <param name="mode">The coordinate frame to solve in (scale operations force Local).</param>
    /// <param name="transform">The transform to manipulate.</param>
    /// <param name="snap">Optional snap settings.</param>
    /// <returns>True when the transform actually changed this frame.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Transform3D transform, GizmoSnap? snap = null)
    {
        bool manipulated = GizmoCore.Manipulate(_context, view, projection, operation, mode, ref transform, snap);
        DrawCurrent();
        return manipulated;
    }

    /// <summary>
    /// Manipulates a 2D transform with the gizmo and draws the handles. The transform
    /// maps to Position = (X, Y, 0), Rotation2D as a Z-axis rotation (engine sign
    /// convention baked in) and Scale = (X, Y, 1); 2D rotation uses
    /// <see cref="GizmoOperation.RotateZ"/>. Works in orthographic mode.
    /// </summary>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="operation">The handles to display and respond to.</param>
    /// <param name="mode">The coordinate frame to solve in (scale operations force Local).</param>
    /// <param name="transform">The transform to manipulate.</param>
    /// <param name="snap">Optional snap settings.</param>
    /// <returns>True when the transform actually changed this frame.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Transform2D transform, GizmoSnap? snap = null)
    {
        bool manipulated = GizmoCore.Manipulate(_context, view, projection, operation, mode, ref transform, snap);
        DrawCurrent();
        return manipulated;
    }

    /// <summary>
    /// Manipulates a row-major model matrix with the gizmo and draws the handles.
    /// </summary>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="operation">The handles to display and respond to.</param>
    /// <param name="mode">The coordinate frame to solve in (scale operations force Local).</param>
    /// <param name="matrix">The matrix to manipulate.</param>
    /// <param name="snap">Optional snap settings.</param>
    /// <returns>True when the matrix actually changed this frame.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Matrix4x4 matrix, GizmoSnap? snap = null)
    {
        return Manipulate(view, projection, operation, mode, ref matrix, out _, snap);
    }

    /// <summary>
    /// Manipulates a row-major model matrix with the gizmo, draws the handles and
    /// outputs the world-space delta applied this frame.
    /// </summary>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="operation">The handles to display and respond to.</param>
    /// <param name="mode">The coordinate frame to solve in (scale operations force Local).</param>
    /// <param name="matrix">The matrix to manipulate.</param>
    /// <param name="deltaMatrix">The delta applied this frame, or identity when unchanged.</param>
    /// <param name="snap">Optional snap settings.</param>
    /// <returns>True when the matrix actually changed this frame.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Matrix4x4 matrix,
        out Matrix4x4 deltaMatrix, GizmoSnap? snap = null)
    {
        bool manipulated = GizmoCore.Manipulate(_context, view, projection, operation, mode, ref matrix, out deltaMatrix, snap);
        DrawCurrent();
        return manipulated;
    }

    /// <summary>
    /// Draws a grid on the model-local XY plane (Z = 0, the Alco ground plane),
    /// clipped against the camera frustum. Must be called inside an ImGui window scope.
    /// </summary>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="model">The model matrix transforming the grid's local XY plane.</param>
    /// <param name="gridSize">Half extent of the grid in local units.</param>
    public static void DrawGrid(in Matrix4x4 view, in Matrix4x4 projection, in Matrix4x4 model, float gridSize)
    {
        GizmoDraw.DrawGrid(_context.Viewport, view, projection, model, gridSize, ImGui.GetWindowDrawList());
    }

    /// <summary>Draws the current call's handles when the last Manipulate produced a valid gizmo.</summary>
    private static void DrawCurrent()
    {
        if (_context.CallValid)
        {
            GizmoDraw.Draw(_context, ImGui.GetWindowDrawList());
        }
    }
}
