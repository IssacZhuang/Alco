using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// Per-frame mouse input snapshot consumed by the gizmo core. The facade fills it
/// from ImGui IO; tests construct it directly so the core stays headless.
/// </summary>
internal readonly struct GizmoInput
{
    /// <summary>Mouse position in screen pixels, origin at the top-left of the viewport.</summary>
    public readonly Vector2 MousePos;

    /// <summary>Whether the left mouse button is currently held down.</summary>
    public readonly bool MouseDown;

    /// <summary>Mouse movement since the previous frame in pixels.</summary>
    public readonly Vector2 MouseDelta;

    /// <summary>
    /// Whether a new drag may be activated this frame. The facade clears this when
    /// another ImGui item is hovered or active so the gizmo does not steal clicks.
    /// </summary>
    public readonly bool AllowActivation;

    /// <summary>
    /// Creates an input snapshot.
    /// </summary>
    /// <param name="mousePos">Mouse position in screen pixels.</param>
    /// <param name="mouseDown">Whether the left mouse button is held down.</param>
    /// <param name="mouseDelta">Mouse movement since the previous frame.</param>
    /// <param name="allowActivation">Whether a new drag may be activated this frame.</param>
    public GizmoInput(Vector2 mousePos, bool mouseDown, Vector2 mouseDelta, bool allowActivation = true)
    {
        MousePos = mousePos;
        MouseDown = mouseDown;
        MouseDelta = mouseDelta;
        AllowActivation = allowActivation;
    }
}
