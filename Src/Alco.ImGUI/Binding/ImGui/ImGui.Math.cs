
using System.Numerics;

namespace Alco.ImGUI;

public static unsafe partial class ImGui
{
    public static bool EditTransform3D(
        ReadOnlySpan<char> label, 
        ref Transform3D transform,
        float positionStep = 0.1f,
        float rotationStep = 1f,
        float scaleStep = 0.1f
        )
    {
        bool isEdited = false;
        ImGui.Text(label);
        ImGui.Separator();
        isEdited |= ImGui.DragFloat3("##Position", ref transform.Position, positionStep);
        Vector3 eulerAngles = math.euler(transform.Rotation);
        isEdited |= ImGui.DragFloat3("##Rotation", ref eulerAngles, rotationStep);
        transform.Rotation = math.quaternion(eulerAngles);
        isEdited |= ImGui.DragFloat3("##Scale", ref transform.Scale, scaleStep);
        return isEdited;
    }

    public static bool EditTransform2D(ReadOnlySpan<char> label, ref Transform2D transform)
    {
        bool isEdited = false;
        ImGui.Text(label);
        ImGui.Separator();
        isEdited |= ImGui.DragFloat2("##Position", ref transform.Position);
        float degree = transform.Rotation.ToDegree();
        isEdited |= ImGui.DragFloat("##Rotation", ref degree);
        transform.Rotation = new Rotation2D(degree);
        isEdited |= ImGui.DragFloat2("##Scale", ref transform.Scale);
        return isEdited;
    }
}

