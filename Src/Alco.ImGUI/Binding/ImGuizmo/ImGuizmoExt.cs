using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Alco.ImGUI;

//ImGuizmo suppoert for .Net math types

/// <summary>
/// ImGuizmo provides 3D manipulation gizmos for transformation operations, supporting .NET math types.
/// </summary>
public static unsafe partial class ImGuizmo
{
    /// <summary>
    /// Decomposes a 4x4 matrix into translation, rotation (euler angles), and scale components.
    /// </summary>
    /// <param name="matrix">The matrix to decompose.</param>
    /// <param name="translation">The resulting translation vector.</param>
    /// <param name="eulerAngles">The resulting rotation as euler angles in degrees.</param>
    /// <param name="scale">The resulting scale vector.</param>
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

    /// <summary>
    /// Creates a transformation matrix from translation, rotation (euler angles), and scale components.
    /// </summary>
    /// <param name="translation">The translation vector.</param>
    /// <param name="eulerAngles">The rotation as euler angles in degrees.</param>
    /// <param name="scale">The scale vector.</param>
    /// <param name="matrix">The resulting transformation matrix.</param>
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

    /// <summary>
    /// Draws a grid in 3D space.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="matrix">The model matrix.</param>
    /// <param name="gridSize">The size of the grid cells.</param>
    public static void DrawGrid(in Matrix4x4 view, in Matrix4x4 projection, in Matrix4x4 matrix, float gridSize)
    {
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        {
            ImGuizmoNative.ImGuizmo_DrawGrid((float*)viewPtr, (float*)projectionPtr, (float*)matrixPtr, gridSize);
        }
    }

    /// <summary>
    /// Draws cubes in 3D space using the provided transformation matrices.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="matrices">An array of transformation matrices for the cubes.</param>
    public static void DrawCubes(in Matrix4x4 view, in Matrix4x4 projection, ReadOnlySpan<Matrix4x4> matrices)
    {
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matricesPtr = matrices)
        {
            ImGuizmoNative.ImGuizmo_DrawCubes((float*)viewPtr, (float*)projectionPtr, (float*)matricesPtr, matrices.Length);
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a transformation matrix.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="matrix">The matrix to manipulate.</param>
    /// <returns>True if the matrix was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Matrix4x4 matrix)
    {
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        {
            return ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, null, null, null) != 0;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a transformation matrix and returns the delta matrix.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="matrix">The matrix to manipulate.</param>
    /// <param name="deltaMatrix">The resulting change in transformation.</param>
    /// <returns>True if the matrix was modified, false otherwise.</returns>
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

    /// <summary>
    /// Performs a manipulation operation on a 3D transform.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="transform">The 3D transform to manipulate.</param>
    /// <returns>True if the transform was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform3D transform)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, null, null, null) != 0;
            if (result)
            {
                DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
                transform.Position = translation;
                transform.Rotation = EulerToQuaternion(eulerAngles);
                transform.Scale = scale;
            }
            return result;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a 2D transform.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="transform">The 2D transform to manipulate.</param>
    /// <returns>True if the transform was modified, false otherwise.</returns>
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
            transform.Rotation = Rotation2D.FromDegree(-eulerAngles.Z);
            transform.Scale = new Vector2(scale.X, scale.Y);
            return result;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a transformation matrix with snapping and returns the delta matrix.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="matrix">The matrix to manipulate.</param>
    /// <param name="deltaMatrix">The resulting change in transformation.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <returns>True if the matrix was modified, false otherwise.</returns>
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

    /// <summary>
    /// Performs a manipulation operation on a transformation matrix with snapping and local bounds.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="matrix">The matrix to manipulate.</param>
    /// <param name="deltaMatrix">The resulting change in transformation.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <param name="localBounds">The local bounds for the manipulation.</param>
    /// <returns>True if the matrix was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Matrix4x4 matrix, out Matrix4x4 deltaMatrix, in Vector3 snap, in BoundingBox3D localBounds)
    {
        deltaMatrix = Matrix4x4.Identity;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        fixed (Matrix4x4* deltaMatrixPtr = &deltaMatrix)
        fixed (Vector3* snapPtr = &snap)
        fixed (BoundingBox3D* localBoundsPtr = &localBounds)
        {
            return ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, (float*)deltaMatrixPtr, (float*)snapPtr, (float*)localBoundsPtr, null) != 0;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a transformation matrix with snapping, local bounds, and bounds snapping.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="matrix">The matrix to manipulate.</param>
    /// <param name="deltaMatrix">The resulting change in transformation.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <param name="localBounds">The local bounds for the manipulation.</param>
    /// <param name="boundsSnap">The snap values for the bounds.</param>
    /// <returns>True if the matrix was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Matrix4x4 matrix, out Matrix4x4 deltaMatrix, in Vector3 snap, in BoundingBox3D localBounds, in Vector3 boundsSnap)
    {
        deltaMatrix = Matrix4x4.Identity;
        deltaMatrix = Matrix4x4.Identity;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Matrix4x4* matrixPtr = &matrix)
        fixed (Matrix4x4* deltaMatrixPtr = &deltaMatrix)
        fixed (Vector3* snapPtr = &snap)
        fixed (BoundingBox3D* localBoundsPtr = &localBounds)
        fixed (Vector3* boundsSnapPtr = &boundsSnap)
        {
            return ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, (float*)deltaMatrixPtr, (float*)snapPtr, (float*)localBoundsPtr, (float*)boundsSnapPtr) != 0;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a 3D transform with snapping.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="transform">The 3D transform to manipulate.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <returns>True if the transform was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform3D transform, in Vector3 snap)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Vector3* snapPtr = &snap)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, (float*)snapPtr, null, null) != 0;
            if (result)
            {
                DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
                transform.Position = translation;
                transform.Rotation = EulerToQuaternion(eulerAngles);
                transform.Scale = scale;
            }
            return result;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a 3D transform with snapping and local bounds.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="transform">The 3D transform to manipulate.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <param name="localBounds">The local bounds for the manipulation.</param>
    /// <returns>True if the transform was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform3D transform, in Vector3 snap, in BoundingBox3D localBounds)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Vector3* snapPtr = &snap)
        fixed (BoundingBox3D* localBoundsPtr = &localBounds)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, (float*)snapPtr, (float*)localBoundsPtr, null) != 0;
            if (result)
            {
                DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
                transform.Position = translation;
                transform.Rotation = EulerToQuaternion(eulerAngles);
                transform.Scale = scale;
            }
            return result;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a 3D transform with snapping, local bounds, and bounds snapping.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="transform">The 3D transform to manipulate.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <param name="localBounds">The local bounds for the manipulation.</param>
    /// <param name="boundsSnap">The snap values for the bounds.</param>
    /// <returns>True if the transform was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform3D transform, in Vector3 snap, in BoundingBox3D localBounds, in Vector3 boundsSnap)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Vector3* snapPtr = &snap)
        fixed (BoundingBox3D* localBoundsPtr = &localBounds)
        fixed (Vector3* boundsSnapPtr = &boundsSnap)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, (float*)snapPtr, (float*)localBoundsPtr, (float*)boundsSnapPtr) != 0;
            if (result)
            {
                DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
                transform.Position = translation;
                transform.Rotation = EulerToQuaternion(eulerAngles);
                transform.Scale = scale;
            }
            return result;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a 2D transform with snapping.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="transform">The 2D transform to manipulate.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <returns>True if the transform was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform2D transform, in Vector3 snap)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Vector3* snapPtr = &snap)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, (float*)snapPtr, null, null) != 0;
            if (result)
            {
                DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
                transform.Position = new Vector2(translation.X, translation.Y);
                transform.Rotation = Rotation2D.FromDegree(-eulerAngles.Z);
                transform.Scale = new Vector2(scale.X, scale.Y);
            }
            return result;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a 2D transform with snapping and local bounds.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="transform">The 2D transform to manipulate.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <param name="localBounds">The local bounds for the manipulation.</param>
    /// <returns>True if the transform was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform2D transform, in Vector3 snap, in BoundingBox3D localBounds)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Vector3* snapPtr = &snap)
        fixed (BoundingBox3D* localBoundsPtr = &localBounds)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, (float*)snapPtr, (float*)localBoundsPtr, null) != 0;
            if (result)
            {
                DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
                transform.Position = new Vector2(translation.X, translation.Y);
                transform.Rotation = Rotation2D.FromDegree(-eulerAngles.Z);
                transform.Scale = new Vector2(scale.X, scale.Y);
            }
            return result;
        }
    }

    /// <summary>
    /// Performs a manipulation operation on a 2D transform with snapping, local bounds, and bounds snapping.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="projection">The projection matrix.</param>
    /// <param name="operation">The type of transformation operation to perform.</param>
    /// <param name="mode">The transformation mode.</param>
    /// <param name="transform">The 2D transform to manipulate.</param>
    /// <param name="snap">The snap values for translation, rotation, and scale.</param>
    /// <param name="localBounds">The local bounds for the manipulation.</param>
    /// <param name="boundsSnap">The snap values for the bounds.</param>
    /// <returns>True if the transform was modified, false otherwise.</returns>
    public static bool Manipulate(in Matrix4x4 view, in Matrix4x4 projection, OPERATION operation, MODE mode, ref Transform2D transform, in Vector3 snap, in BoundingBox3D localBounds, in Vector3 boundsSnap)
    {
        Matrix4x4 matrix = transform.Matrix;
        fixed (Matrix4x4* viewPtr = &view)
        fixed (Matrix4x4* projectionPtr = &projection)
        fixed (Vector3* snapPtr = &snap)
        fixed (BoundingBox3D* localBoundsPtr = &localBounds)
        fixed (Vector3* boundsSnapPtr = &boundsSnap)
        {
            Matrix4x4* matrixPtr = &matrix;
            bool result = ImGuizmoNative.ImGuizmo_Manipulate((float*)viewPtr, (float*)projectionPtr, operation, mode, (float*)matrixPtr, null, (float*)snapPtr, (float*)localBoundsPtr, (float*)boundsSnapPtr) != 0;
            if (result)
            {
                DecomposeMatrixToComponents(matrix, out Vector3 translation, out Vector3 eulerAngles, out Vector3 scale);
                transform.Position = new Vector2(translation.X, translation.Y);
                transform.Rotation = Rotation2D.FromDegree(-eulerAngles.Z);
                transform.Scale = new Vector2(scale.X, scale.Y);
            }
            return result;
        }
    }

    /// <summary>
    /// Manipulates the view matrix based on user input.
    /// </summary>
    /// <param name="view">The view matrix to manipulate.</param>
    /// <param name="length">The camera distance.</param>
    /// <param name="position">The position of the view manipulation widget.</param>
    /// <param name="size">The size of the view manipulation widget.</param>
    /// <param name="backgroundColor">The background color of the widget.</param>
    public static void ViewManipulate(ref Matrix4x4 view, float length, Vector2 position, Vector2 size, uint backgroundColor)
    {
        fixed (Matrix4x4* viewPtr = &view)
        {
            ImGuizmoNative.ImGuizmo_ViewManipulate((float*)viewPtr, length, position, size, backgroundColor);
        }
    }

    private static Quaternion EulerToQuaternion(Vector3 eulerAngles)
    {
        //not efficient but no need to optimize because only one matrix can be manipulated at a time
        Matrix4x4 rotationMatrix = Matrix4x4.CreateRotationX(math.TORADIANS * eulerAngles.X) *
                                   Matrix4x4.CreateRotationY(math.TORADIANS * eulerAngles.Y) *
                                   Matrix4x4.CreateRotationZ(math.TORADIANS * eulerAngles.Z);
        return Quaternion.CreateFromRotationMatrix(rotationMatrix);
    }
}