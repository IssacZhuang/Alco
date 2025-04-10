
using System.Numerics;

namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
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

