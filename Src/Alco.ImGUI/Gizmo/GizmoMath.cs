using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// Pure static math for the gizmo: TRS/matrix conversion (row-major), world/screen
/// projection using the same <c>(1 - y)</c> flip as <see cref="CameraMathUtility"/>,
/// camera ray computation, ray/plane and point/segment helpers, and snap rounding.
/// All functions are free of ImGui types and run headless.
/// </summary>
internal static class GizmoMath
{
    /// <summary>Machine epsilon for float, matching the FLT_EPSILON used by the reference implementation.</summary>
    public const float Epsilon = 1.192092896e-07f;

    /// <summary>Snap hysteresis ratio; values within half a step snap to the step.</summary>
    public const float SnapTension = 0.5f;

    /// <summary>Unit vectors of the three world axes, indexed by axis.</summary>
    public static readonly Vector3[] DirectionUnary = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };

    /// <summary>Returns component <paramref name="index"/> of the vector (0 = X, 1 = Y, 2 = Z).</summary>
    public static float Component(in Vector3 v, int index)
    {
        return index switch
        {
            0 => v.X,
            1 => v.Y,
            _ => v.Z,
        };
    }

    /// <summary>Returns the axis row (right/up/dir for 0/1/2) of a row-major matrix as a 3D vector.</summary>
    public static Vector3 AxisRow(in Matrix4x4 m, int index)
    {
        return index switch
        {
            0 => new Vector3(m.M11, m.M12, m.M13),
            1 => new Vector3(m.M21, m.M22, m.M23),
            _ => new Vector3(m.M31, m.M32, m.M33),
        };
    }

    /// <summary>Returns the translation row of a row-major matrix.</summary>
    public static Vector3 Translation(in Matrix4x4 m)
    {
        return new Vector3(m.M41, m.M42, m.M43);
    }

    /// <summary>Normalizes the three axis rows of the matrix in place.</summary>
    public static void OrthoNormalize(ref Matrix4x4 m)
    {
        Vector3 right = NormalizeSafe(AxisRow(m, 0));
        Vector3 up = NormalizeSafe(AxisRow(m, 1));
        Vector3 dir = NormalizeSafe(AxisRow(m, 2));
        m.M11 = right.X; m.M12 = right.Y; m.M13 = right.Z;
        m.M21 = up.X; m.M22 = up.Y; m.M23 = up.Z;
        m.M31 = dir.X; m.M32 = dir.Y; m.M33 = dir.Z;
    }

    /// <summary>Normalizes the vector; returns a zero vector when the length is degenerate.</summary>
    public static Vector3 NormalizeSafe(in Vector3 v)
    {
        float length = v.Length();
        return length > Epsilon ? v / length : Vector3.Zero;
    }

    /// <summary>
    /// Attempts to decompose a row-major TRS matrix into translation, rotation and scale.
    /// Fails when any axis row is degenerate (zero scale axis).
    /// </summary>
    /// <param name="matrix">The matrix to decompose.</param>
    /// <param name="translation">The extracted translation.</param>
    /// <param name="rotation">The extracted rotation.</param>
    /// <param name="scale">The extracted scale (axis row lengths).</param>
    /// <returns>True when the matrix could be decomposed.</returns>
    public static bool TryDecompose(in Matrix4x4 matrix, out Vector3 translation, out Quaternion rotation, out Vector3 scale)
    {
        translation = Translation(matrix);
        Vector3 right = AxisRow(matrix, 0);
        Vector3 up = AxisRow(matrix, 1);
        Vector3 dir = AxisRow(matrix, 2);
        scale = new Vector3(right.Length(), up.Length(), dir.Length());
        if (scale.X < Epsilon || scale.Y < Epsilon || scale.Z < Epsilon)
        {
            rotation = Quaternion.Identity;
            return false;
        }

        right /= scale.X;
        up /= scale.Y;
        dir /= scale.Z;
        Matrix4x4 rotationMatrix = Matrix4x4.Identity;
        rotationMatrix.M11 = right.X; rotationMatrix.M12 = right.Y; rotationMatrix.M13 = right.Z;
        rotationMatrix.M21 = up.X; rotationMatrix.M22 = up.Y; rotationMatrix.M23 = up.Z;
        rotationMatrix.M31 = dir.X; rotationMatrix.M32 = dir.Y; rotationMatrix.M33 = dir.Z;
        rotation = Quaternion.CreateFromRotationMatrix(rotationMatrix);
        return true;
    }

    /// <summary>
    /// Recomposes translation, rotation and scale into a row-major TRS matrix.
    /// </summary>
    /// <param name="translation">The translation.</param>
    /// <param name="rotation">The rotation.</param>
    /// <param name="scale">The scale.</param>
    /// <returns>The composed matrix.</returns>
    public static Matrix4x4 Recompose(in Vector3 translation, in Quaternion rotation, in Vector3 scale)
    {
        return math.matrix4trs(translation, rotation, scale);
    }

    /// <summary>
    /// Attempts to decompose a row-major matrix produced by 2D gizmo manipulation back
    /// into 2D transform components. The rotation sign convention matches
    /// <see cref="Transform2D.Matrix"/>: a positive <see cref="Rotation2D"/> angle rotates
    /// the local X axis toward -Y in world space (clockwise on screen for a Y-up camera).
    /// Fails when any axis row is degenerate.
    /// </summary>
    /// <param name="matrix">The matrix to decompose.</param>
    /// <param name="position">The extracted 2D position.</param>
    /// <param name="rotation">The extracted 2D rotation.</param>
    /// <param name="scale">The extracted 2D scale.</param>
    /// <returns>True when the matrix could be decomposed.</returns>
    public static bool TryDecompose2D(in Matrix4x4 matrix, out Vector2 position, out Rotation2D rotation, out Vector2 scale)
    {
        position = new Vector2(matrix.M41, matrix.M42);
        Vector3 right = AxisRow(matrix, 0);
        Vector3 up = AxisRow(matrix, 1);
        Vector3 dir = AxisRow(matrix, 2);
        float scaleX = right.Length();
        float scaleY = up.Length();
        float scaleZ = dir.Length();
        if (scaleX < Epsilon || scaleY < Epsilon || scaleZ < Epsilon)
        {
            rotation = Rotation2D.Identity;
            scale = Vector2.Zero;
            return false;
        }

        // matrix4rotation(Rotation2D) stores M11 = cos, M12 = -sin on the first row.
        float invScaleX = 1f / scaleX;
        float cos = matrix.M11 * invScaleX;
        float minusSin = matrix.M12 * invScaleX;
        rotation = new Rotation2D(-minusSin, cos);
        scale = new Vector2(scaleX, scaleY);
        return true;
    }

    /// <summary>
    /// Projects a world-space point to screen pixels using the same NDC-to-screen
    /// <c>(1 - y)</c> flip convention as <see cref="CameraMathUtility.WorldPointToScreen2D"/>.
    /// </summary>
    /// <param name="worldPos">The world-space point.</param>
    /// <param name="mvp">The matrix transforming world space to clip space.</param>
    /// <param name="viewport">The viewport in pixels.</param>
    /// <returns>The screen position in pixels.</returns>
    public static Vector2 WorldToScreen(in Vector3 worldPos, in Matrix4x4 mvp, Rect viewport)
    {
        Vector4 clip = Vector4.Transform(worldPos, mvp);
        float invW = 1f / clip.W;
        float ndcX = clip.X * invW;
        float ndcY = clip.Y * invW;
        return new Vector2(
            (ndcX * 0.5f + 0.5f) * viewport.Size.X + viewport.Origin.X,
            (1f - (ndcY * 0.5f + 0.5f)) * viewport.Size.Y + viewport.Origin.Y);
    }

    /// <summary>
    /// Computes the world-space camera ray under a screen point by unprojecting the
    /// near and far clip planes. Works for both perspective and orthographic projections.
    /// </summary>
    /// <param name="mousePos">The screen point in pixels.</param>
    /// <param name="viewProjInverse">Inverse of the view-projection matrix.</param>
    /// <param name="reversed">Whether the projection uses reversed depth.</param>
    /// <param name="viewport">The viewport in pixels.</param>
    /// <param name="rayOrigin">The resulting ray origin (on the near plane for perspective).</param>
    /// <param name="rayDir">The resulting normalized ray direction.</param>
    public static void ComputeCameraRay(in Vector2 mousePos, in Matrix4x4 viewProjInverse, bool reversed, Rect viewport, out Vector3 rayOrigin, out Vector3 rayDir)
    {
        float mox = ((mousePos.X - viewport.Origin.X) / viewport.Size.X) * 2f - 1f;
        float moy = (1f - (mousePos.Y - viewport.Origin.Y) / viewport.Size.Y) * 2f - 1f;

        float zNear = reversed ? 1f - Epsilon : 0f;
        float zFar = reversed ? 0f : 1f - Epsilon;

        Vector4 origin = Vector4.Transform(new Vector4(mox, moy, zNear, 1f), viewProjInverse);
        origin /= origin.W;
        Vector4 end = Vector4.Transform(new Vector4(mox, moy, zFar, 1f), viewProjInverse);
        end /= end.W;

        rayOrigin = new Vector3(origin.X, origin.Y, origin.Z);
        rayDir = NormalizeSafe(new Vector3(end.X - origin.X, end.Y - origin.Y, end.Z - origin.Z));
    }

    /// <summary>
    /// Measures the clip-space length of a world-space segment, correcting for the
    /// viewport aspect ratio.
    /// </summary>
    /// <param name="start">Segment start in the space of <paramref name="mvp"/>.</param>
    /// <param name="end">Segment end in the space of <paramref name="mvp"/>.</param>
    /// <param name="mvp">The matrix transforming to clip space.</param>
    /// <param name="displayRatio">Viewport width divided by height.</param>
    /// <returns>The segment length in clip space units.</returns>
    public static float GetSegmentLengthClipSpace(in Vector3 start, in Vector3 end, in Matrix4x4 mvp, float displayRatio)
    {
        Vector4 startOfSegment = Vector4.Transform(start, mvp);
        if (MathF.Abs(startOfSegment.W) > Epsilon)
        {
            startOfSegment /= startOfSegment.W;
        }

        Vector4 endOfSegment = Vector4.Transform(end, mvp);
        if (MathF.Abs(endOfSegment.W) > Epsilon)
        {
            endOfSegment /= endOfSegment.W;
        }

        Vector3 clipSpaceAxis = new Vector3(endOfSegment.X, endOfSegment.Y, endOfSegment.Z)
            - new Vector3(startOfSegment.X, startOfSegment.Y, startOfSegment.Z);
        if (displayRatio < 1.0f)
        {
            clipSpaceAxis.X *= displayRatio;
        }
        else
        {
            clipSpaceAxis.Y /= displayRatio;
        }

        return MathF.Sqrt(clipSpaceAxis.X * clipSpaceAxis.X + clipSpaceAxis.Y * clipSpaceAxis.Y);
    }

    /// <summary>
    /// Measures the clip-space area of the parallelogram spanned by two world-space vectors.
    /// </summary>
    /// <param name="ptO">Common origin of the two vectors.</param>
    /// <param name="ptA">End of the first vector.</param>
    /// <param name="ptB">End of the second vector.</param>
    /// <param name="mvp">The matrix transforming to clip space.</param>
    /// <param name="displayRatio">Viewport width divided by height.</param>
    /// <returns>The parallelogram area in clip space units.</returns>
    public static float GetParallelogram(in Vector3 ptO, in Vector3 ptA, in Vector3 ptB, in Matrix4x4 mvp, float displayRatio)
    {
        Vector4 pO = Vector4.Transform(ptO, mvp);
        Vector4 pA = Vector4.Transform(ptA, mvp);
        Vector4 pB = Vector4.Transform(ptB, mvp);
        if (MathF.Abs(pO.W) > Epsilon) { pO /= pO.W; }
        if (MathF.Abs(pA.W) > Epsilon) { pA /= pA.W; }
        if (MathF.Abs(pB.W) > Epsilon) { pB /= pB.W; }

        Vector2 segA = new Vector2(pA.X - pO.X, pA.Y - pO.Y);
        Vector2 segB = new Vector2(pB.X - pO.X, pB.Y - pO.Y);
        segA.Y /= displayRatio;
        segB.Y /= displayRatio;
        Vector2 segAOrtho = Vector2.Normalize(new Vector2(-segA.Y, segA.X));
        float dt = Vector2.Dot(segAOrtho, segB);
        return MathF.Sqrt(segA.X * segA.X + segA.Y * segA.Y) * MathF.Abs(dt);
    }

    /// <summary>
    /// Builds a plane (xyz = normalized normal, w = distance offset) from a point and a normal.
    /// </summary>
    /// <param name="point">A point on the plane.</param>
    /// <param name="normal">The plane normal (need not be normalized).</param>
    /// <returns>The plane.</returns>
    public static Vector4 BuildPlan(in Vector3 point, in Vector3 normal)
    {
        Vector3 n = NormalizeSafe(normal);
        return new Vector4(n, Vector3.Dot(n, point));
    }

    /// <summary>
    /// Intersects a ray with a plane.
    /// </summary>
    /// <param name="rayOrigin">The ray origin.</param>
    /// <param name="rayDir">The ray direction.</param>
    /// <param name="plane">The plane (xyz = normal, w = offset).</param>
    /// <returns>The signed distance along the ray, or -1 when the ray is parallel to the plane.</returns>
    public static float IntersectRayPlane(in Vector3 rayOrigin, in Vector3 rayDir, in Vector4 plane)
    {
        Vector3 normal = new Vector3(plane.X, plane.Y, plane.Z);
        float numer = Vector3.Dot(normal, rayOrigin) - plane.W;
        float denom = Vector3.Dot(normal, rayDir);
        if (MathF.Abs(denom) < Epsilon)
        {
            return -1.0f;
        }

        return -(numer / denom);
    }

    /// <summary>Returns the signed distance from a point to a plane.</summary>
    public static float DistanceToPlane(in Vector3 point, in Vector4 plane)
    {
        return Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), point) + plane.W;
    }

    /// <summary>Returns the closest point on a segment to a given point.</summary>
    public static Vector2 PointOnSegment(in Vector2 point, in Vector2 vertPos1, in Vector2 vertPos2)
    {
        Vector2 c = point - vertPos1;
        Vector2 v = vertPos2 - vertPos1;
        float d = v.Length();
        if (d < Epsilon)
        {
            return vertPos1;
        }

        v /= d;
        float t = Vector2.Dot(v, c);
        if (t < 0f)
        {
            return vertPos1;
        }

        if (t > d)
        {
            return vertPos2;
        }

        return vertPos1 + v * t;
    }

    /// <summary>
    /// Snaps a value to a multiple of the step with a half-step hysteresis.
    /// Steps &lt;= 0 leave the value untouched.
    /// </summary>
    /// <param name="value">The value to snap.</param>
    /// <param name="snap">The snap step.</param>
    public static void ComputeSnap(ref float value, float snap)
    {
        if (snap <= Epsilon)
        {
            return;
        }

        float modulo = value % snap;
        float moduloRatio = MathF.Abs(modulo) / snap;
        if (moduloRatio < SnapTension)
        {
            value -= modulo;
        }
        else if (moduloRatio > 1f - SnapTension)
        {
            value = value - modulo + snap * (value < 0f ? -1f : 1f);
        }
    }

    /// <summary>Snaps each component of a vector to a multiple of its step; components with step &lt;= 0 are skipped.</summary>
    public static void ComputeSnap(ref Vector3 value, in Vector3 snap)
    {
        float x = value.X;
        float y = value.Y;
        float z = value.Z;
        ComputeSnap(ref x, snap.X);
        ComputeSnap(ref y, snap.Y);
        ComputeSnap(ref z, snap.Z);
        value = new Vector3(x, y, z);
    }

    /// <summary>
    /// Computes the six frustum planes of a clip matrix (xyz = outward normal, w = offset).
    /// </summary>
    /// <param name="clip">The view-projection matrix.</param>
    /// <param name="frustum">Destination for the six planes.</param>
    public static void ComputeFrustumPlanes(in Matrix4x4 clip, Span<Vector4> frustum)
    {
        frustum[0] = new Vector4(clip.M14 - clip.M11, clip.M24 - clip.M21, clip.M34 - clip.M31, clip.M44 - clip.M41);
        frustum[1] = new Vector4(clip.M14 + clip.M11, clip.M24 + clip.M21, clip.M34 + clip.M31, clip.M44 + clip.M41);
        frustum[2] = new Vector4(clip.M14 - clip.M12, clip.M24 - clip.M22, clip.M34 - clip.M32, clip.M44 - clip.M42);
        frustum[3] = new Vector4(clip.M14 + clip.M12, clip.M24 + clip.M22, clip.M34 + clip.M32, clip.M44 + clip.M42);
        frustum[4] = new Vector4(clip.M14 - clip.M13, clip.M24 - clip.M23, clip.M34 - clip.M33, clip.M44 - clip.M43);
        frustum[5] = new Vector4(clip.M14 + clip.M13, clip.M24 + clip.M23, clip.M34 + clip.M33, clip.M44 + clip.M43);

        for (int i = 0; i < 6; i++)
        {
            Vector4 plane = frustum[i];
            Vector3 normal = new Vector3(plane.X, plane.Y, plane.Z);
            float length = normal.Length();
            float invLength = 1f / (length > Epsilon ? length : Epsilon);
            frustum[i] = plane * invLength;
        }
    }
}
