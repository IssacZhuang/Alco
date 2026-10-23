
using System.Numerics;

namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
    /// <summary>Draws editable position, rotation and scale controls for a 3D transform.</summary>
    /// <param name="transform">The transform to edit.</param>
    /// <param name="positionStep">The drag step for position.</param>
    /// <param name="rotationStep">The drag step for rotation in degrees.</param>
    /// <param name="scaleStep">The drag step for scale.</param>
    /// <returns>True when any component was edited.</returns>
    public static bool EditTransform3D(
        ref Transform3D transform,
        float positionStep = 0.1f,
        float rotationStep = 1f,
        float scaleStep = 0.1f
        )
    {
        bool isEdited = false;
        isEdited |= ImGui.DragFloat3("Position", ref transform.Position, positionStep);
        Vector3 eulerAngles = math.euler(transform.Rotation);
        isEdited |= ImGui.DragFloat3("Rotation", ref eulerAngles, rotationStep);
        transform.Rotation = math.quaternion(eulerAngles);
        isEdited |= ImGui.DragFloat3("Scale", ref transform.Scale, scaleStep);
        return isEdited;
    }

    /// <summary>Draws editable position, rotation and scale controls for a 2D transform.</summary>
    /// <param name="transform">The transform to edit.</param>
    /// <param name="positionStep">The drag step for position.</param>
    /// <param name="rotationStep">The drag step for rotation in degrees.</param>
    /// <param name="scaleStep">The drag step for scale.</param>
    /// <returns>True when any component was edited.</returns>
    public static bool EditTransform2D(
        ref Transform2D transform,
        float positionStep = 0.1f,
        float rotationStep = 1f,
        float scaleStep = 0.1f
        )
    {
        bool isEdited = false;
        isEdited |= ImGui.DragFloat2("Position", ref transform.Position, positionStep);
        float degree = transform.Rotation.ToDegree();
        isEdited |= ImGui.DragFloat("Rotation", ref degree, rotationStep);
        transform.Rotation = new Rotation2D(degree);
        isEdited |= ImGui.DragFloat2("Scale", ref transform.Scale, scaleStep);
        return isEdited;
    }
}

