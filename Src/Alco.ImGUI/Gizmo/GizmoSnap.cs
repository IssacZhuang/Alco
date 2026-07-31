using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// Snap settings for gizmo manipulation. A component less than or equal to zero
/// disables snapping for that component.
/// </summary>
public readonly struct GizmoSnap
{
    /// <summary>
    /// Translation snap step in world units, per axis. A component &lt;= 0 disables
    /// snapping on that axis. Snapping applies to the cumulative displacement since
    /// drag start, so the drag-start grid offset is preserved.
    /// </summary>
    public Vector3 Translation { get; }

    /// <summary>
    /// Rotation snap step in degrees. &lt;= 0 disables rotation snapping.
    /// Snapping applies to the cumulative angle since drag start.
    /// </summary>
    public float RotationDegrees { get; }

    /// <summary>Scale snap step (multiplier delta). &lt;= 0 disables scale snapping.</summary>
    public float Scale { get; }

    /// <summary>
    /// Creates a snap setting with per-axis translation steps.
    /// </summary>
    /// <param name="translation">Translation snap step per axis in world units.</param>
    /// <param name="rotationDegrees">Rotation snap step in degrees.</param>
    /// <param name="scale">Scale snap step.</param>
    public GizmoSnap(Vector3 translation, float rotationDegrees = 0f, float scale = 0f)
    {
        Translation = translation;
        RotationDegrees = rotationDegrees;
        Scale = scale;
    }

    /// <summary>
    /// Creates a snap setting with the same translation step on all axes.
    /// </summary>
    /// <param name="translation">Translation snap step in world units.</param>
    /// <param name="rotationDegrees">Rotation snap step in degrees.</param>
    /// <param name="scale">Scale snap step.</param>
    /// <returns>The snap setting.</returns>
    public static GizmoSnap Uniform(float translation, float rotationDegrees = 0f, float scale = 0f)
    {
        return new GizmoSnap(new Vector3(translation), rotationDegrees, scale);
    }

    /// <summary>
    /// Creates a 2D snap setting with steps on X and Y and no Z snapping.
    /// </summary>
    /// <param name="x">Translation snap step on X in world units.</param>
    /// <param name="y">Translation snap step on Y in world units.</param>
    /// <returns>The snap setting.</returns>
    public static GizmoSnap XY(float x, float y)
    {
        return new GizmoSnap(new Vector3(x, y, 0f));
    }
}
