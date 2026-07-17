namespace Alco.ImGUI;

/// <summary>
/// Coordinate frame in which gizmo handles are displayed and solved.
/// </summary>
public enum GizmoMode
{
    /// <summary>Handles follow the manipulated object's rotation.</summary>
    Local,

    /// <summary>Handles stay aligned with the world axes.</summary>
    World,
}
