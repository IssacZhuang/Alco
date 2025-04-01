using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Alco.ImGUI;

//ImGuizmo suppoert for .Net math types

public static unsafe partial class ImGuizmo
{
    public static void DecomposeMatrixToComponents(in Matrix4x4 matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale)
    {
        fixed (Matrix4x4* matrixPtr = &matrix)
        fixed (Vector3* translationPtr = &translation)
        fixed (Vector3* eulerAnglesPtr = &eulerAngles)
        fixed (Vector3* scalePtr = &scale)
        {
            ImGuizmoNative.ImGuizmo_DecomposeMatrixToComponents((float*)matrixPtr, (float*)translationPtr, (float*)eulerAnglesPtr, (float*)scalePtr);
        }
    }

    public static void RecomposeMatrixFromComponents(in Vector3 translation, in Vector3 eulerAngles, in Vector3 scale, out Matrix4x4 matrix)
    {
        fixed (Matrix4x4* matrixPtr = &matrix)
        fixed (Vector3* translationPtr = &translation)
        fixed (Vector3* eulerAnglesPtr = &eulerAngles)
        fixed (Vector3* scalePtr = &scale)
        {
            ImGuizmoNative.ImGuizmo_RecomposeMatrixFromComponents((float*)translationPtr, (float*)eulerAnglesPtr, (float*)scalePtr, (float*)matrixPtr);
        }
    }

    public static void DrawGrid(in Matrix4x4 view, in Matrix4x4 projection, in Matrix4x4 matrix, float gridSize)
    {
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        {
            ImGuizmoNative.ImGuizmo_DrawGrid((float*)viewPtr, (float*)projectionPtr, (float*)matrixPtr, gridSize);
        }
    }

    public static void DrawCubes(in Matrix4x4 view, in Matrix4x4 projection, ReadOnlySpan<Matrix4x4> matrices)
    {
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matricesPtr = matrices)
        {
            ImGuizmoNative.ImGuizmo_DrawCubes((float*)viewPtr, (float*)projectionPtr, (float*)matricesPtr, matrices.Length);
        }
    }

    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Matrix4x4 matrix)
    {
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        {
            return ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, null, null, null) != 0;
        }
    }

    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Matrix4x4 matrix, out Matrix4x4 deltaMatrix)
    {
        deltaMatrix = Matrix4x4.Identity;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        fixed (Matrix4x4* deltaMatrixPtr = &deltaMatrix)
        {
            return ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, (float*)deltaMatrixPtr, null, null, null) != 0;
        }
    }

    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform3D transform)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, null, null, null) != 0;
            DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
            transform.Position = translation;
            transform.Rotation = math.euler(math.radians(eulerAngles));
            transform.Scale = scale;
            return result;
        }
    }

    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform2D transform)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, null, null, null) != 0;
            DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
            transform.Position = new Vector2(translation.X, translation.Y);
            transform.Rotation = Rotation2D.FromDegree(eulerAngles.Z);
            transform.Scale = new Vector2(scale.X, scale.Y);
            return result;
        }
    }

    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Matrix4x4 matrix, out Matrix4x4 deltaMatrix, in Vector3 snap)
    {
        deltaMatrix = Matrix4x4.Identity;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        fixed (Matrix4x4* deltaMatrixPtr = &deltaMatrix)
        fixed (Vector3* snapPtr = &snap)
        {
            return ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, (float*)deltaMatrixPtr, (float*)snapPtr, null, null) != 0;
        }
    }

    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Matrix4x4 matrix, out Matrix4x4 deltaMatrix, in Vector3 snap, ReadOnlySpan<Vector3> localBounds)
    {
        if (localBounds.Length != 2)
            throw new ArgumentException("localBounds span must contain exactly 2 vectors (min and max bounds)", nameof(localBounds));

        deltaMatrix = Matrix4x4.Identity;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        fixed (Matrix4x4* deltaMatrixPtr = &deltaMatrix)
        fixed (Vector3* snapPtr = &snap)
        fixed (Vector3* localBoundsPtr = localBounds)
        {
            return ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, (float*)deltaMatrixPtr, (float*)snapPtr, (float*)localBoundsPtr, null) != 0;
        }
    }

    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Matrix4x4 matrix, out Matrix4x4 deltaMatrix, in Vector3 snap, ReadOnlySpan<Vector3> localBounds, in Vector3 boundsSnap)
    {
        if (localBounds.Length != 2)
            throw new ArgumentException("localBounds span must contain exactly 2 vectors (min and max bounds)", nameof(localBounds));

        deltaMatrix = Matrix4x4.Identity;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        fixed (Matrix4x4* deltaMatrixPtr = &deltaMatrix)
        fixed (Vector3* snapPtr = &snap)
        fixed (Vector3* localBoundsPtr = localBounds)
        fixed (Vector3* boundsSnapPtr = &boundsSnap)
        {
            return ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, (float*)deltaMatrixPtr, (float*)snapPtr, (float*)localBoundsPtr, (float*)boundsSnapPtr) != 0;
        }
    }

    public static void ViewManipulate(ref Matrix4x4 view, float length, Vector2 position, Vector2 size, uint backgroundColor)
    {
        fixed (Matrix4x4* viewPtr = &view)
        {
            ImGuizmoNative.ImGuizmo_ViewManipulate((float*)viewPtr, length, position, size, backgroundColor);
        }
    }
}