using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// The headless gizmo frame pipeline: per-call context computation (matrix
/// decomposition, camera ray, screen factor), handle hit-testing, drag solving and
/// snapping, and writing the result back. Contains no ImGui references; input
/// arrives through <see cref="GizmoContext.Input"/> and drawing is a separate layer
/// invoked by the facade.
/// </summary>
internal static class GizmoCore
{
    /// <summary>Target gizmo size in clip space, matching the reference implementation default.</summary>
    private const float GizmoSizeClipSpace = 0.1f;

    /// <summary>Minimum parallelogram area in clip space for a plane handle to be visible and hittable.</summary>
    private const float AxisLimit = 0.0025f;

    /// <summary>Minimum axis length in clip space for an axis handle to be visible.</summary>
    private const float PlaneLimit = 0.02f;

    /// <summary>Scale factor for the rotation rings relative to the translation handles.</summary>
    private const float RotationDisplayFactor = 1.2f;

    /// <summary>Minimum plane handle extent, in gizmo-sized units.</summary>
    private const float QuadMin = 0.5f;

    /// <summary>Maximum plane handle extent, in gizmo-sized units.</summary>
    private const float QuadMax = 0.8f;

    /// <summary>Hit-test distance of axis handles in pixels.</summary>
    private const float AxisHitPixelSize = 12f;

    /// <summary>Hit-test distance of rotation ring handles in pixels.</summary>
    private const float RingHitPixelSize = 8f;

    /// <summary>Hit-test inner distance of the uniform scale ring in pixels.</summary>
    private const float UniformScaleHitMin = 17f;

    /// <summary>Hit-test outer distance of the uniform scale ring in pixels.</summary>
    private const float UniformScaleHitMax = 23f;

    /// <summary>Plane translation handles required per plane-normal axis (YZ, XZ, XY).</summary>
    public static readonly GizmoOperation[] TranslatePlans =
    {
        GizmoOperation.TranslateYZ,
        GizmoOperation.TranslateXZ,
        GizmoOperation.TranslateXY,
    };

    private static bool Intersects(GizmoOperation op, GizmoOperation flag)
    {
        return (op & flag) != 0;
    }

    private static bool Contains(GizmoOperation op, GizmoOperation flags)
    {
        return (op & flags) == flags;
    }

    private static bool IsTranslateType(GizmoMoveType type)
    {
        return type >= GizmoMoveType.MoveX && type <= GizmoMoveType.MoveScreen;
    }

    private static bool IsRotateType(GizmoMoveType type)
    {
        return type >= GizmoMoveType.RotateX && type <= GizmoMoveType.RotateScreen;
    }

    private static bool IsScaleType(GizmoMoveType type)
    {
        return type >= GizmoMoveType.ScaleX && type <= GizmoMoveType.ScaleXYZ;
    }

    private static bool CanActivate(GizmoContext ctx)
    {
        return ctx.Input.MouseDown && !ctx.PreviousMouseDown && ctx.Input.AllowActivation;
    }

    private static float DisplayRatio(GizmoContext ctx)
    {
        return ctx.Viewport.Size.Y > GizmoMath.Epsilon ? ctx.Viewport.Size.X / ctx.Viewport.Size.Y : 1f;
    }

    /// <summary>
    /// Runs one manipulation frame on a matrix: recomputes the per-call context,
    /// hit-tests handles, solves the active drag and writes the result back.
    /// </summary>
    /// <param name="ctx">The gizmo context holding frame input and drag state.</param>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="operation">The handles to display and respond to.</param>
    /// <param name="mode">The coordinate frame to solve in (scale operations force Local).</param>
    /// <param name="matrix">The model matrix to manipulate.</param>
    /// <param name="deltaMatrix">The world-space delta applied this frame, or identity.</param>
    /// <param name="snap">Optional snap settings.</param>
    /// <returns>True when the matrix actually changed this frame.</returns>
    public static bool Manipulate(GizmoContext ctx, in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Matrix4x4 matrix, out Matrix4x4 deltaMatrix, GizmoSnap? snap)
    {
        deltaMatrix = Matrix4x4.Identity;
        ctx.CallValid = false;
        ctx.Operation = operation;

        // A bounds drag owns the mouse: gizmo handles are neither solved nor drawn
        // (same as the reference implementation).
        if (ctx.UsingBounds)
        {
            return false;
        }

        bool hasScaleOp = (operation & (GizmoOperation.Scale | GizmoOperation.ScaleUniform)) != 0;
        if (!ComputeContext(ctx, view, projection, matrix, hasScaleOp ? GizmoMode.Local : mode))
        {
            return false;
        }

        // The gizmo origin projects behind the camera: no drawing, no interaction.
        Vector4 camSpacePosition = Vector4.Transform(Vector3.Zero, ctx.Mvp);
        if (!ctx.IsOrthographic && camSpacePosition.Z < 0.001f && !ctx.Using)
        {
            return false;
        }

        ctx.CallValid = true;

        GizmoMoveType type = GizmoMoveType.None;
        bool manipulated = HandleTranslation(ctx, ref matrix, ref deltaMatrix, operation, ref type, snap)
            || HandleScale(ctx, ref matrix, ref deltaMatrix, operation, ref type, snap)
            || HandleRotation(ctx, ref matrix, ref deltaMatrix, operation, ref type, snap);

        ctx.CallType = type;
        if (type != GizmoMoveType.None)
        {
            ctx.FrameHoverType = type;
        }

        return manipulated;
    }

    /// <summary>
    /// Runs one manipulation frame on a 3D transform. The transform is composed to a
    /// matrix, manipulated and decomposed back; when decomposition fails the
    /// transform is left untouched and the call reports no change.
    /// </summary>
    /// <param name="ctx">The gizmo context holding frame input and drag state.</param>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="operation">The handles to display and respond to.</param>
    /// <param name="mode">The coordinate frame to solve in (scale operations force Local).</param>
    /// <param name="transform">The transform to manipulate.</param>
    /// <param name="snap">Optional snap settings.</param>
    /// <returns>True when the transform actually changed this frame.</returns>
    public static bool Manipulate(GizmoContext ctx, in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Transform3D transform, GizmoSnap? snap)
    {
        Matrix4x4 matrix = transform.Matrix;
        bool manipulated = Manipulate(ctx, view, projection, operation, mode, ref matrix, out _, snap);
        if (!manipulated)
        {
            return false;
        }

        if (!GizmoMath.TryDecompose(matrix, out Vector3 translation, out Quaternion rotation, out Vector3 scale))
        {
            return false;
        }

        transform.Position = translation;
        transform.Rotation = rotation;
        transform.Scale = scale;
        return true;
    }

    /// <summary>
    /// Runs one manipulation frame on a 2D transform. The transform maps to a matrix
    /// with Position = (X, Y, 0), Rotation2D as a Z-axis rotation (engine sign
    /// convention baked in) and Scale = (X, Y, 1); 2D rotation uses
    /// <see cref="GizmoOperation.RotateZ"/>. Works in orthographic mode.
    /// </summary>
    /// <param name="ctx">The gizmo context holding frame input and drag state.</param>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="operation">The handles to display and respond to.</param>
    /// <param name="mode">The coordinate frame to solve in (scale operations force Local).</param>
    /// <param name="transform">The transform to manipulate.</param>
    /// <param name="snap">Optional snap settings.</param>
    /// <returns>True when the transform actually changed this frame.</returns>
    public static bool Manipulate(GizmoContext ctx, in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Transform2D transform, GizmoSnap? snap)
    {
        Matrix4x4 matrix = transform.Matrix;
        bool manipulated = Manipulate(ctx, view, projection, operation, mode, ref matrix, out _, snap);
        if (!manipulated)
        {
            return false;
        }

        if (!GizmoMath.TryDecompose2D(matrix, out Vector2 position, out Rotation2D rotation, out Vector2 scale))
        {
            return false;
        }

        transform.Position = position;
        transform.Rotation = rotation;
        transform.Scale = scale;
        return true;
    }

    /// <summary>
    /// Recomputes the per-call working set: display model, matrices, camera basis,
    /// reversed-depth detection, screen factor and the camera ray under the mouse.
    /// </summary>
    /// <returns>False when any matrix involved is not invertible (degenerate input).</returns>
    private static bool ComputeContext(GizmoContext ctx, in Matrix4x4 view, in Matrix4x4 projection, in Matrix4x4 matrix, GizmoMode mode)
    {
        ctx.Mode = mode;
        ctx.ViewMatrix = view;
        ctx.ProjectionMatrix = projection;

        ctx.ModelLocal = matrix;
        GizmoMath.OrthoNormalize(ref ctx.ModelLocal);
        ctx.Model = mode == GizmoMode.Local ? ctx.ModelLocal : Matrix4x4.CreateTranslation(GizmoMath.Translation(matrix));
        ctx.ModelSource = matrix;
        ctx.ModelScaleOrigin = new Vector3(
            GizmoMath.AxisRow(matrix, 0).Length(),
            GizmoMath.AxisRow(matrix, 1).Length(),
            GizmoMath.AxisRow(matrix, 2).Length());

        if (!Matrix4x4.Invert(ctx.Model, out ctx.ModelInverse))
        {
            return false;
        }

        if (!Matrix4x4.Invert(ctx.ModelSource, out ctx.ModelSourceInverse))
        {
            return false;
        }

        ctx.ViewProjection = view * projection;
        ctx.Mvp = ctx.Model * ctx.ViewProjection;
        ctx.MvpLocal = ctx.ModelLocal * ctx.ViewProjection;

        if (!Matrix4x4.Invert(view, out Matrix4x4 viewInverse))
        {
            return false;
        }

        ctx.CameraDir = GizmoMath.AxisRow(viewInverse, 2);
        ctx.CameraEye = GizmoMath.Translation(viewInverse);
        ctx.CameraRight = GizmoMath.AxisRow(viewInverse, 0);
        ctx.CameraUp = GizmoMath.AxisRow(viewInverse, 1);

        // Detect reversed-depth projections.
        Vector4 nearPos = Vector4.Transform(new Vector4(0f, 0f, 1f, 1f), projection);
        Vector4 farPos = Vector4.Transform(new Vector4(0f, 0f, 2f, 1f), projection);
        ctx.Reversed = nearPos.Z / nearPos.W > farPos.Z / farPos.W;

        // Screen factor: world size that keeps the gizmo at a constant clip-space size.
        Vector3 rightViewInverse = Vector3.TransformNormal(ctx.CameraRight, ctx.ModelInverse);
        float rightLength = GizmoMath.GetSegmentLengthClipSpace(Vector3.Zero, rightViewInverse, ctx.Mvp, DisplayRatio(ctx));
        if (rightLength < GizmoMath.Epsilon)
        {
            return false;
        }

        ctx.ScreenFactor = GizmoSizeClipSpace / rightLength;

        Vector2 centerSSpace = GizmoMath.WorldToScreen(Vector3.Zero, ctx.Mvp, ctx.Viewport);
        ctx.ScreenSquareCenter = centerSSpace;
        ctx.ScreenSquareMin = centerSSpace - new Vector2(10f);
        ctx.ScreenSquareMax = centerSSpace + new Vector2(10f);

        if (!Matrix4x4.Invert(ctx.ViewProjection, out Matrix4x4 viewProjInverse))
        {
            return false;
        }

        GizmoMath.ComputeCameraRay(ctx.Input.MousePos, viewProjInverse, ctx.Reversed, ctx.Viewport, out ctx.RayOrigin, out ctx.RayVector);
        return true;
    }

    /// <summary>
    /// Computes the axis direction, plane axes and visibility of one tripod axis,
    /// flipping axes toward the camera. While dragging, the factors captured at
    /// activation are reused so handles do not flip mid-drag.
    /// </summary>
    /// <param name="ctx">The gizmo context.</param>
    /// <param name="axisIndex">The axis index (0 = X, 1 = Y, 2 = Z).</param>
    /// <param name="dirAxis">The axis direction in model space.</param>
    /// <param name="dirPlaneX">The first plane axis in model space.</param>
    /// <param name="dirPlaneY">The second plane axis in model space.</param>
    /// <param name="belowAxisLimit">Whether the axis handle is long enough on screen to be visible.</param>
    /// <param name="belowPlaneLimit">Whether the plane handle is large enough on screen to be visible and hittable.</param>
    /// <param name="localCoordinates">Whether to measure against the full model rotation (used by scale handles).</param>
    public static void ComputeTripodAxisAndVisibility(GizmoContext ctx, int axisIndex,
        out Vector3 dirAxis, out Vector3 dirPlaneX, out Vector3 dirPlaneY,
        out bool belowAxisLimit, out bool belowPlaneLimit, bool localCoordinates)
    {
        dirAxis = GizmoMath.DirectionUnary[axisIndex];
        dirPlaneX = GizmoMath.DirectionUnary[(axisIndex + 1) % 3];
        dirPlaneY = GizmoMath.DirectionUnary[(axisIndex + 2) % 3];

        if (ctx.Using)
        {
            // When using, use stored factors so the gizmo doesn't flip when we translate.
            belowAxisLimit = ctx.BelowAxisLimit[axisIndex];
            belowPlaneLimit = ctx.BelowPlaneLimit[axisIndex];
            dirAxis *= ctx.AxisFactor[axisIndex];
            dirPlaneX *= ctx.AxisFactor[(axisIndex + 1) % 3];
            dirPlaneY *= ctx.AxisFactor[(axisIndex + 2) % 3];
            return;
        }

        Matrix4x4 mvp = localCoordinates ? ctx.MvpLocal : ctx.Mvp;
        float displayRatio = DisplayRatio(ctx);

        float lenDir = GizmoMath.GetSegmentLengthClipSpace(Vector3.Zero, dirAxis, mvp, displayRatio);
        float lenDirMinus = GizmoMath.GetSegmentLengthClipSpace(Vector3.Zero, -dirAxis, mvp, displayRatio);
        float lenDirPlaneX = GizmoMath.GetSegmentLengthClipSpace(Vector3.Zero, dirPlaneX, mvp, displayRatio);
        float lenDirMinusPlaneX = GizmoMath.GetSegmentLengthClipSpace(Vector3.Zero, -dirPlaneX, mvp, displayRatio);
        float lenDirPlaneY = GizmoMath.GetSegmentLengthClipSpace(Vector3.Zero, dirPlaneY, mvp, displayRatio);
        float lenDirMinusPlaneY = GizmoMath.GetSegmentLengthClipSpace(Vector3.Zero, -dirPlaneY, mvp, displayRatio);

        float mulAxis = lenDir < lenDirMinus && MathF.Abs(lenDir - lenDirMinus) > GizmoMath.Epsilon ? -1f : 1f;
        float mulAxisX = lenDirPlaneX < lenDirMinusPlaneX && MathF.Abs(lenDirPlaneX - lenDirMinusPlaneX) > GizmoMath.Epsilon ? -1f : 1f;
        float mulAxisY = lenDirPlaneY < lenDirMinusPlaneY && MathF.Abs(lenDirPlaneY - lenDirMinusPlaneY) > GizmoMath.Epsilon ? -1f : 1f;
        dirAxis *= mulAxis;
        dirPlaneX *= mulAxisX;
        dirPlaneY *= mulAxisY;

        float axisLengthInClipSpace = GizmoMath.GetSegmentLengthClipSpace(Vector3.Zero, dirAxis * ctx.ScreenFactor, mvp, displayRatio);
        float paraSurf = GizmoMath.GetParallelogram(Vector3.Zero, dirPlaneX * ctx.ScreenFactor, dirPlaneY * ctx.ScreenFactor, mvp, displayRatio);

        belowPlaneLimit = paraSurf > AxisLimit;
        belowAxisLimit = axisLengthInClipSpace > PlaneLimit;

        ctx.AxisFactor[axisIndex] = mulAxis;
        ctx.AxisFactor[(axisIndex + 1) % 3] = mulAxisX;
        ctx.AxisFactor[(axisIndex + 2) % 3] = mulAxisY;
        ctx.BelowAxisLimit[axisIndex] = belowAxisLimit;
        ctx.BelowPlaneLimit[axisIndex] = belowPlaneLimit;
    }

    /// <summary>Hit-tests translation handles against the context mouse position.</summary>
    private static GizmoMoveType GetMoveType(GizmoContext ctx, GizmoOperation op)
    {
        if (!Intersects(op, GizmoOperation.Translate) || ctx.Using)
        {
            return GizmoMoveType.None;
        }

        Vector2 mouse = ctx.Input.MousePos;
        if (!ctx.Viewport.Contains(mouse))
        {
            return GizmoMoveType.None;
        }

        GizmoMoveType type = GizmoMoveType.None;
        Vector3 modelPos = GizmoMath.Translation(ctx.Model);

        // Center square: screen-space move, only when all three translate axes are enabled.
        if (mouse.X >= ctx.ScreenSquareMin.X && mouse.X <= ctx.ScreenSquareMax.X
            && mouse.Y >= ctx.ScreenSquareMin.Y && mouse.Y <= ctx.ScreenSquareMax.Y
            && Contains(op, GizmoOperation.Translate))
        {
            type = GizmoMoveType.MoveScreen;
        }

        for (int i = 0; i < 3 && type == GizmoMoveType.None; i++)
        {
            ComputeTripodAxisAndVisibility(ctx, i,
                out Vector3 dirAxis, out Vector3 dirPlaneX, out Vector3 dirPlaneY,
                out _, out bool belowPlaneLimit, false);
            dirAxis = Vector3.TransformNormal(dirAxis, ctx.Model);
            dirPlaneX = Vector3.TransformNormal(dirPlaneX, ctx.Model);
            dirPlaneY = Vector3.TransformNormal(dirPlaneY, ctx.Model);

            float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, GizmoMath.BuildPlan(modelPos, dirAxis));
            Vector3 posOnPlan = ctx.RayOrigin + ctx.RayVector * len;

            Vector2 axisStartOnScreen = GizmoMath.WorldToScreen(modelPos + dirAxis * (ctx.ScreenFactor * 0.1f), ctx.ViewProjection, ctx.Viewport);
            Vector2 axisEndOnScreen = GizmoMath.WorldToScreen(modelPos + dirAxis * ctx.ScreenFactor, ctx.ViewProjection, ctx.Viewport);

            Vector2 closestPointOnAxis = GizmoMath.PointOnSegment(mouse, axisStartOnScreen, axisEndOnScreen);
            if ((closestPointOnAxis - mouse).Length() < AxisHitPixelSize
                && Intersects(op, (GizmoOperation)((int)GizmoOperation.TranslateX << i)))
            {
                type = GizmoMoveType.MoveX + i;
            }

            float dx = Vector3.Dot(dirPlaneX, (posOnPlan - modelPos) * (1f / ctx.ScreenFactor));
            float dy = Vector3.Dot(dirPlaneY, (posOnPlan - modelPos) * (1f / ctx.ScreenFactor));
            if (belowPlaneLimit && dx >= QuadMin && dx <= QuadMax && dy >= QuadMin && dy <= QuadMax && Contains(op, TranslatePlans[i]))
            {
                type = GizmoMoveType.MoveYZ + i;
            }
        }

        return type;
    }

    /// <summary>Hit-tests rotation handles against the context mouse position.</summary>
    private static GizmoMoveType GetRotateType(GizmoContext ctx, GizmoOperation op)
    {
        if (ctx.Using)
        {
            return GizmoMoveType.None;
        }

        Vector2 mouse = ctx.Input.MousePos;
        if (!ctx.Viewport.Contains(mouse))
        {
            return GizmoMoveType.None;
        }

        GizmoMoveType type = GizmoMoveType.None;

        float dist = (mouse - ctx.ScreenSquareCenter).Length();
        if (Intersects(op, GizmoOperation.RotateScreen)
            && dist >= ctx.RadiusSquareCenter - 4f && dist < ctx.RadiusSquareCenter + 4f)
        {
            type = GizmoMoveType.RotateScreen;
        }

        Vector3 modelPos = GizmoMath.Translation(ctx.Model);
        Vector3 modelViewPos = Vector3.Transform(modelPos, ctx.ViewMatrix);

        for (int i = 0; i < 3 && type == GizmoMoveType.None; i++)
        {
            if (!Intersects(op, (GizmoOperation)((int)GizmoOperation.RotateX << i)))
            {
                continue;
            }

            Vector4 pickupPlan = GizmoMath.BuildPlan(modelPos, GizmoMath.AxisRow(ctx.Model, i));
            float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, pickupPlan);
            Vector3 intersectWorldPos = ctx.RayOrigin + ctx.RayVector * len;
            Vector3 intersectViewPos = Vector3.Transform(intersectWorldPos, ctx.ViewMatrix);

            // Only the camera-facing half of the ring is hittable.
            if (MathF.Abs(modelViewPos.Z) - MathF.Abs(intersectViewPos.Z) < -GizmoMath.Epsilon)
            {
                continue;
            }

            Vector3 localPos = intersectWorldPos - modelPos;
            Vector3 idealPosOnCircle = GizmoMath.NormalizeSafe(localPos);
            idealPosOnCircle = Vector3.TransformNormal(idealPosOnCircle, ctx.ModelInverse);
            Vector2 idealPosOnCircleScreen = GizmoMath.WorldToScreen(
                idealPosOnCircle * (RotationDisplayFactor * ctx.ScreenFactor), ctx.Mvp, ctx.Viewport);

            if ((idealPosOnCircleScreen - mouse).Length() < RingHitPixelSize)
            {
                type = GizmoMoveType.RotateX + i;
            }
        }

        return type;
    }

    /// <summary>Hit-tests scale handles against the context mouse position.</summary>
    private static GizmoMoveType GetScaleType(GizmoContext ctx, GizmoOperation op)
    {
        if (ctx.Using)
        {
            return GizmoMoveType.None;
        }

        Vector2 mouse = ctx.Input.MousePos;
        if (!ctx.Viewport.Contains(mouse))
        {
            return GizmoMoveType.None;
        }

        GizmoMoveType type = GizmoMoveType.None;
        Vector3 modelLocalPos = GizmoMath.Translation(ctx.ModelLocal);

        // Center square: uniform scale, only when all three per-axis scale handles are enabled.
        if (mouse.X >= ctx.ScreenSquareMin.X && mouse.X <= ctx.ScreenSquareMax.X
            && mouse.Y >= ctx.ScreenSquareMin.Y && mouse.Y <= ctx.ScreenSquareMax.Y
            && Contains(op, GizmoOperation.Scale))
        {
            type = GizmoMoveType.ScaleXYZ;
        }

        for (int i = 0; i < 3 && type == GizmoMoveType.None; i++)
        {
            if (!Intersects(op, (GizmoOperation)((int)GizmoOperation.ScaleX << i)))
            {
                continue;
            }

            ComputeTripodAxisAndVisibility(ctx, i,
                out Vector3 dirAxis, out _, out _, out _, out _, true);
            dirAxis = Vector3.TransformNormal(dirAxis, ctx.ModelLocal);

            float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, GizmoMath.BuildPlan(modelLocalPos, dirAxis));
            Vector3 posOnPlan = ctx.RayOrigin + ctx.RayVector * len;

            bool hasTranslateOnAxis = Contains(op, (GizmoOperation)((int)GizmoOperation.TranslateX << i));
            float startOffset = hasTranslateOnAxis ? 1.0f : 0.1f;
            float endOffset = hasTranslateOnAxis ? 1.4f : 1.0f;
            Vector2 posOnPlanScreen = GizmoMath.WorldToScreen(posOnPlan, ctx.ViewProjection, ctx.Viewport);
            Vector2 axisStartOnScreen = GizmoMath.WorldToScreen(modelLocalPos + dirAxis * (ctx.ScreenFactor * startOffset), ctx.ViewProjection, ctx.Viewport);
            Vector2 axisEndOnScreen = GizmoMath.WorldToScreen(modelLocalPos + dirAxis * (ctx.ScreenFactor * endOffset), ctx.ViewProjection, ctx.Viewport);

            Vector2 closestPointOnAxis = GizmoMath.PointOnSegment(mouse, axisStartOnScreen, axisEndOnScreen);
            if ((closestPointOnAxis - mouse).Length() < AxisHitPixelSize)
            {
                type = GizmoMoveType.ScaleX + i;
            }
        }

        // Uniform scale ring around the center.
        float dist = (mouse - ctx.ScreenSquareCenter).Length();
        if (Intersects(op, GizmoOperation.ScaleUniform) && dist >= UniformScaleHitMin && dist < UniformScaleHitMax)
        {
            type = GizmoMoveType.ScaleXYZ;
        }

        return type;
    }

    /// <summary>Activates, solves and releases translation drags.</summary>
    private static bool HandleTranslation(GizmoContext ctx, ref Matrix4x4 matrix, ref Matrix4x4 deltaMatrix,
        GizmoOperation op, ref GizmoMoveType type, GizmoSnap? snap)
    {
        if (!Intersects(op, GizmoOperation.Translate) || type != GizmoMoveType.None)
        {
            return false;
        }

        bool applyRotationLocally = ctx.Mode == GizmoMode.Local || type == GizmoMoveType.MoveScreen;
        bool modified = false;
        Vector3 modelPos = GizmoMath.Translation(ctx.Model);

        if (ctx.Using && IsTranslateType(ctx.CurrentOperation))
        {
            float signedLength = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, ctx.TranslationPlan);
            float len = MathF.Abs(signedLength);
            Vector3 newPos = ctx.RayOrigin + ctx.RayVector * len;

            Vector3 newOrigin = newPos - ctx.RelativeOrigin * ctx.ScreenFactor;
            Vector3 delta = newOrigin - modelPos;

            if (ctx.CurrentOperation >= GizmoMoveType.MoveX && ctx.CurrentOperation <= GizmoMoveType.MoveZ)
            {
                int axisIndex = ctx.CurrentOperation - GizmoMoveType.MoveX;
                Vector3 axisValue = GizmoMath.AxisRow(ctx.Model, axisIndex);
                float lengthOnAxis = Vector3.Dot(axisValue, delta);
                delta = axisValue * lengthOnAxis;
            }

            // Snap the cumulative displacement since drag start, preserving the start offset.
            if (snap.HasValue)
            {
                Vector3 cumulativeDelta = modelPos + delta - ctx.MatrixOrigin;
                if (applyRotationLocally)
                {
                    Matrix4x4 modelSourceNormalized = ctx.ModelSource;
                    GizmoMath.OrthoNormalize(ref modelSourceNormalized);
                    if (Matrix4x4.Invert(modelSourceNormalized, out Matrix4x4 modelSourceNormalizedInverse))
                    {
                        cumulativeDelta = Vector3.TransformNormal(cumulativeDelta, modelSourceNormalizedInverse);
                        GizmoMath.ComputeSnap(ref cumulativeDelta, snap.Value.Translation);
                        cumulativeDelta = Vector3.TransformNormal(cumulativeDelta, modelSourceNormalized);
                    }
                    else
                    {
                        GizmoMath.ComputeSnap(ref cumulativeDelta, snap.Value.Translation);
                    }
                }
                else
                {
                    GizmoMath.ComputeSnap(ref cumulativeDelta, snap.Value.Translation);
                }

                delta = ctx.MatrixOrigin + cumulativeDelta - modelPos;
            }

            if (delta != ctx.TranslationLastDelta)
            {
                modified = true;
            }

            ctx.TranslationLastDelta = delta;

            deltaMatrix = Matrix4x4.CreateTranslation(delta);
            matrix = ctx.ModelSource * deltaMatrix;

            if (!ctx.Input.MouseDown)
            {
                ctx.Using = false;
            }

            type = ctx.CurrentOperation;
        }
        else
        {
            // Find a new possible way to move.
            type = ctx.OverGizmoHotspot ? GizmoMoveType.None : GetMoveType(ctx, op);
            ctx.OverGizmoHotspot |= type != GizmoMoveType.None;
            if (CanActivate(ctx) && type != GizmoMoveType.None)
            {
                ctx.Using = true;
                ctx.CurrentOperation = type;
                Vector3[] movePlanNormal =
                {
                    GizmoMath.AxisRow(ctx.Model, 0), GizmoMath.AxisRow(ctx.Model, 1), GizmoMath.AxisRow(ctx.Model, 2),
                    GizmoMath.AxisRow(ctx.Model, 0), GizmoMath.AxisRow(ctx.Model, 1), GizmoMath.AxisRow(ctx.Model, 2),
                    -ctx.CameraDir,
                };
                Vector3 cameraToModelNormalized = GizmoMath.NormalizeSafe(modelPos - ctx.CameraEye);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 orthoVector = Vector3.Cross(movePlanNormal[i], cameraToModelNormalized);
                    movePlanNormal[i] = GizmoMath.NormalizeSafe(Vector3.Cross(movePlanNormal[i], orthoVector));
                }

                ctx.TranslationPlan = GizmoMath.BuildPlan(modelPos, movePlanNormal[type - GizmoMoveType.MoveX]);
                float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, ctx.TranslationPlan);
                ctx.TranslationPlanOrigin = ctx.RayOrigin + ctx.RayVector * len;
                ctx.MatrixOrigin = modelPos;
                ctx.RelativeOrigin = (ctx.TranslationPlanOrigin - modelPos) * (1f / ctx.ScreenFactor);
                ctx.TranslationLastDelta = Vector3.Zero;
            }
        }

        return modified;
    }

    /// <summary>Activates, solves and releases scale drags. Scale always solves in the local frame.</summary>
    private static bool HandleScale(GizmoContext ctx, ref Matrix4x4 matrix, ref Matrix4x4 deltaMatrix,
        GizmoOperation op, ref GizmoMoveType type, GizmoSnap? snap)
    {
        bool hasScaleOp = Intersects(op, GizmoOperation.Scale) || Intersects(op, GizmoOperation.ScaleUniform);
        bool mouseOver = ctx.Viewport.Contains(ctx.Input.MousePos) || ctx.Using;
        if (!hasScaleOp || type != GizmoMoveType.None || !mouseOver)
        {
            return false;
        }

        bool modified = false;
        Vector3 modelLocalPos = GizmoMath.Translation(ctx.ModelLocal);

        if (!ctx.Using)
        {
            type = ctx.OverGizmoHotspot ? GizmoMoveType.None : GetScaleType(ctx, op);
            ctx.OverGizmoHotspot |= type != GizmoMoveType.None;
            if (CanActivate(ctx) && type != GizmoMoveType.None)
            {
                ctx.Using = true;
                ctx.CurrentOperation = type;
                Span<Vector3> movePlanNormal = stackalloc Vector3[7]
                {
                    GizmoMath.AxisRow(ctx.ModelLocal, 1), GizmoMath.AxisRow(ctx.ModelLocal, 2), GizmoMath.AxisRow(ctx.ModelLocal, 0),
                    GizmoMath.AxisRow(ctx.ModelLocal, 2), GizmoMath.AxisRow(ctx.ModelLocal, 1), GizmoMath.AxisRow(ctx.ModelLocal, 0),
                    -ctx.CameraDir,
                };
                ctx.TranslationPlan = GizmoMath.BuildPlan(modelLocalPos, movePlanNormal[type - GizmoMoveType.ScaleX]);
                float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, ctx.TranslationPlan);
                ctx.TranslationPlanOrigin = ctx.RayOrigin + ctx.RayVector * len;
                ctx.MatrixOrigin = modelLocalPos;
                ctx.Scale = Vector3.One;
                ctx.ScaleLast = Vector3.One;
                ctx.RelativeOrigin = (ctx.TranslationPlanOrigin - modelLocalPos) * (1f / ctx.ScreenFactor);
                ctx.ScaleValueOrigin = new Vector3(
                    GizmoMath.AxisRow(ctx.ModelSource, 0).Length(),
                    GizmoMath.AxisRow(ctx.ModelSource, 1).Length(),
                    GizmoMath.AxisRow(ctx.ModelSource, 2).Length());
                ctx.SaveMousePosX = ctx.Input.MousePos.X;
            }
        }

        if (ctx.Using && IsScaleType(ctx.CurrentOperation))
        {
            float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, ctx.TranslationPlan);
            Vector3 newPos = ctx.RayOrigin + ctx.RayVector * len;
            Vector3 newOrigin = newPos - ctx.RelativeOrigin * ctx.ScreenFactor;
            Vector3 delta = newOrigin - modelLocalPos;

            if (ctx.CurrentOperation >= GizmoMoveType.ScaleX && ctx.CurrentOperation <= GizmoMoveType.ScaleZ)
            {
                int axisIndex = ctx.CurrentOperation - GizmoMoveType.ScaleX;
                Vector3 axisValue = GizmoMath.AxisRow(ctx.ModelLocal, axisIndex);
                float lengthOnAxis = Vector3.Dot(axisValue, delta);
                delta = axisValue * lengthOnAxis;

                Vector3 baseVector = ctx.TranslationPlanOrigin - modelLocalPos;
                float denominator = Vector3.Dot(axisValue, baseVector);
                float ratio = MathF.Abs(denominator) > 1e-6f
                    ? Vector3.Dot(axisValue, baseVector + delta) / denominator
                    : 1f;
                SetComponent(ref ctx.Scale, axisIndex, MathF.Max(ratio, 0.001f));
            }
            else
            {
                float scaleDelta = (ctx.Input.MousePos.X - ctx.SaveMousePosX) * 0.01f;
                ctx.Scale = new Vector3(MathF.Max(1f + scaleDelta, 0.001f));
            }

            if (snap.HasValue)
            {
                Vector3 scaleSnap = new Vector3(snap.Value.Scale);
                GizmoMath.ComputeSnap(ref ctx.Scale, scaleSnap);
            }

            ctx.Scale = Vector3.Max(ctx.Scale, new Vector3(0.001f));

            if (ctx.ScaleLast != ctx.Scale)
            {
                modified = true;
            }

            ctx.ScaleLast = ctx.Scale;

            Matrix4x4 deltaMatrixScale = Matrix4x4.CreateScale(ctx.Scale * ctx.ScaleValueOrigin);
            matrix = deltaMatrixScale * ctx.ModelLocal;

            Vector3 deltaScale = ctx.Scale * ctx.ScaleValueOrigin;
            Vector3 originalScaleDivider = new Vector3(
                1f / ctx.ModelScaleOrigin.X,
                1f / ctx.ModelScaleOrigin.Y,
                1f / ctx.ModelScaleOrigin.Z);
            deltaScale *= originalScaleDivider;
            deltaMatrix = Matrix4x4.CreateScale(deltaScale);

            if (!ctx.Input.MouseDown)
            {
                ctx.Using = false;
                ctx.Scale = Vector3.One;
            }

            type = ctx.CurrentOperation;
        }

        return modified;
    }

    /// <summary>
    /// Runs one manipulation frame on a world-space axis-aligned box: selects the
    /// camera-facing box faces, hit-tests the corner/edge anchors, solves the active
    /// resize drag and writes the result back. Corner anchors resize two face axes at
    /// once around the opposite corner, edge-midpoint anchors resize a single axis
    /// around the opposite edge midpoint.
    /// </summary>
    /// <param name="ctx">The gizmo context holding frame input and drag state.</param>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="bounds">The world-space box to manipulate.</param>
    /// <param name="snap">Optional per-axis snap steps for the box size; components &lt;= 0 skip snapping.</param>
    /// <param name="infoComponentCount">Component count (2 or 3) shown in the drag info text.</param>
    /// <returns>True when the box actually changed this frame.</returns>
    public static bool ManipulateBounds(GizmoContext ctx, in Matrix4x4 view, in Matrix4x4 projection,
        ref BoundingBox3D bounds, Vector3? snap, int infoComponentCount)
    {
        ctx.CallBoundsValid = false;

        // A gizmo drag owns the mouse: bounds are neither drawn nor interactive
        // (same as the reference implementation).
        if (ctx.Using)
        {
            return false;
        }

        BoundingBox3D origin = bounds;
        if (!ComputeContext(ctx, view, projection, Matrix4x4.Identity, GizmoMode.World))
        {
            return false;
        }

        // The box center projects behind the camera: no drawing, no interaction.
        // Raw clip-space Z (no perspective divide), same check as Manipulate.
        Vector4 centerClip = Vector4.Transform(origin.Center, ctx.ViewProjection);
        if (!ctx.IsOrthographic && centerClip.Z < 0.001f && !ctx.UsingBounds)
        {
            return false;
        }

        ctx.BoundsInfoComponentCount = infoComponentCount;
        ctx.BoundsCurrent = origin;
        ctx.CallBoundsValid = true;

        if (ctx.UsingBounds)
        {
            // Only the face captured at activation stays drawn and solved while dragging.
            ctx.BoundsCallFaceCount = 1;
            ctx.BoundsCallFaceAxis[0] = ctx.BoundsBestAxis;
            ctx.BoundsCallFaceMax[0] = ctx.BoundsFaceMax;

            bool modified = SolveBoundsDrag(ctx, ref bounds, snap);
            ctx.BoundsCurrent = bounds;
            if (!ctx.Input.MouseDown)
            {
                ctx.UsingBounds = false;
            }

            return modified;
        }

        SelectBoundsFaces(ctx, origin);
        TryActivateBoundsDrag(ctx, origin);
        return false;
    }

    /// <summary>
    /// Selects the box faces to draw: world axes whose direction is sufficiently
    /// aligned with the box-to-camera direction (visible faces), best-aligned first,
    /// each on the side of the box facing the camera. Falls back to the single
    /// best-aligned face when the box is viewed edge-on.
    /// </summary>
    private static void SelectBoundsFaces(GizmoContext ctx, in BoundingBox3D bounds)
    {
        Vector3 toCamera = ctx.IsOrthographic
            ? -ctx.CameraDir
            : GizmoMath.NormalizeSafe(ctx.CameraEye - bounds.Center);

        Span<int> candidates = stackalloc int[3];
        int count = 0;
        int bestAxis = 0;
        float bestDot = -1f;
        for (int axis = 0; axis < 3; axis++)
        {
            float dot = MathF.Abs(Vector3.Dot(toCamera, GizmoMath.DirectionUnary[axis]));
            if (dot > bestDot)
            {
                bestDot = dot;
                bestAxis = axis;
            }

            if (dot >= 0.1f)
            {
                candidates[count++] = axis;
            }
        }

        if (count == 0)
        {
            ctx.BoundsCallFaceCount = 1;
            ctx.BoundsCallFaceAxis[0] = bestAxis;
            ctx.BoundsCallFaceMax[0] = Vector3.Dot(toCamera, GizmoMath.DirectionUnary[bestAxis]) > 0f;
            return;
        }

        // Draw the best-aligned face first, matching the reference draw order.
        int bestIndex = 0;
        for (int i = 0; i < count; i++)
        {
            if (candidates[i] == bestAxis)
            {
                bestIndex = i;
                break;
            }
        }

        (candidates[0], candidates[bestIndex]) = (candidates[bestIndex], candidates[0]);

        ctx.BoundsCallFaceCount = count;
        for (int i = 0; i < count; i++)
        {
            ctx.BoundsCallFaceAxis[i] = candidates[i];
            ctx.BoundsCallFaceMax[i] = Vector3.Dot(toCamera, GizmoMath.DirectionUnary[candidates[i]]) > 0f;
        }
    }

    /// <summary>
    /// Hit-tests the corner (two-axis resize) and edge-midpoint (single-axis resize)
    /// anchors of the drawn faces and starts a bounds drag on click.
    /// </summary>
    private static void TryActivateBoundsDrag(GizmoContext ctx, in BoundingBox3D bounds)
    {
        if (!CanActivate(ctx))
        {
            return;
        }

        Vector2 mouse = ctx.Input.MousePos;
        float hoverRadiusSq = ctx.Style.BoundsAnchorBigRadius * ctx.Style.BoundsAnchorBigRadius;

        for (int face = 0; face < ctx.BoundsCallFaceCount; face++)
        {
            int axis = ctx.BoundsCallFaceAxis[face];
            bool faceMax = ctx.BoundsCallFaceMax[face];
            int secondAxis = (axis + 1) % 3;
            int thirdAxis = (axis + 2) % 3;

            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 cornerPos = BoundsFaceCorner(bounds, axis, faceMax, corner);
                Vector3 nextCornerPos = BoundsFaceCorner(bounds, axis, faceMax, (corner + 1) % 4);
                Vector2 cornerScreen = GizmoMath.WorldToScreen(cornerPos, ctx.ViewProjection, ctx.Viewport);
                Vector2 nextCornerScreen = GizmoMath.WorldToScreen(nextCornerPos, ctx.ViewProjection, ctx.Viewport);
                if (!ctx.Viewport.Contains(cornerScreen) || !ctx.Viewport.Contains(nextCornerScreen))
                {
                    continue;
                }

                bool overCorner = (cornerScreen - mouse).LengthSquared() <= hoverRadiusSq;
                bool overMidpoint = ((cornerScreen + nextCornerScreen) * 0.5f - mouse).LengthSquared() <= hoverRadiusSq;
                // Regular gizmo handles under the cursor take priority over the anchors.
                if (ctx.FrameHoverType != GizmoMoveType.None)
                {
                    overCorner = false;
                    overMidpoint = false;
                }

                if (overCorner)
                {
                    ctx.UsingBounds = true;
                    ctx.BoundsBestAxis = axis;
                    ctx.BoundsFaceMax = faceMax;
                    ctx.BoundsOrigin = bounds;
                    ctx.BoundsAnchor = cornerPos;
                    ctx.BoundsPivot = BoundsFaceCorner(bounds, axis, faceMax, (corner + 2) % 4);
                    ctx.BoundsPlan = GizmoMath.BuildPlan(cornerPos, GizmoMath.DirectionUnary[axis]);
                    ctx.BoundsAxis0 = secondAxis;
                    ctx.BoundsAxis1 = thirdAxis;
                    return;
                }

                if (overMidpoint)
                {
                    // Grabbing an edge midpoint resizes the axis perpendicular to the edge.
                    int resizedAxis = corner % 2 == 0 ? secondAxis : thirdAxis;
                    Vector3 oppositeMidpoint = (BoundsFaceCorner(bounds, axis, faceMax, (corner + 2) % 4)
                        + BoundsFaceCorner(bounds, axis, faceMax, (corner + 3) % 4)) * 0.5f;
                    ctx.UsingBounds = true;
                    ctx.BoundsBestAxis = axis;
                    ctx.BoundsFaceMax = faceMax;
                    ctx.BoundsOrigin = bounds;
                    ctx.BoundsAnchor = (cornerPos + nextCornerPos) * 0.5f;
                    ctx.BoundsPivot = oppositeMidpoint;
                    ctx.BoundsPlan = GizmoMath.BuildPlan(ctx.BoundsAnchor, GizmoMath.DirectionUnary[axis]);
                    ctx.BoundsAxis0 = resizedAxis;
                    ctx.BoundsAxis1 = -1;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Solves the active bounds drag: projects the mouse on the captured solve plane,
    /// converts the mouse offset into per-axis size ratios against the drag-start box
    /// and rebuilds the box around the fixed pivot.
    /// </summary>
    private static bool SolveBoundsDrag(GizmoContext ctx, ref BoundingBox3D bounds, Vector3? snap)
    {
        float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, ctx.BoundsPlan);
        Vector3 newPos = ctx.RayOrigin + ctx.RayVector * len;

        Vector3 deltaVector = Vector3.Abs(newPos - ctx.BoundsPivot);
        Vector3 referenceVector = Vector3.Abs(ctx.BoundsAnchor - ctx.BoundsPivot);

        BoundingBox3D solved = ctx.BoundsOrigin;
        for (int i = 0; i < 2; i++)
        {
            int axis = i == 0 ? ctx.BoundsAxis0 : ctx.BoundsAxis1;
            if (axis < 0)
            {
                continue;
            }

            Vector3 axisDir = GizmoMath.DirectionUnary[axis];
            float referenceSize = Vector3.Dot(axisDir, referenceVector);
            float ratio = referenceSize > GizmoMath.Epsilon
                ? Vector3.Dot(axisDir, deltaVector) / referenceSize
                : 1f;

            float originSize = GizmoMath.Component(ctx.BoundsOrigin.Size, axis);
            if (snap.HasValue && originSize > GizmoMath.Epsilon)
            {
                float length = originSize * ratio;
                float snapped = GizmoMath.Component(snap.Value, axis);
                GizmoMath.ComputeSnap(ref length, snapped);
                ratio = length / originSize;
            }

            float pivotCoord = GizmoMath.Component(ctx.BoundsPivot, axis);
            float anchorCoord = GizmoMath.Component(ctx.BoundsAnchor, axis);
            float newCoord = pivotCoord + (anchorCoord - pivotCoord) * ratio;
            Vector3 min = GizmoMath.WithComponent(solved.Min, axis, MathF.Min(pivotCoord, newCoord));
            Vector3 max = GizmoMath.WithComponent(solved.Max, axis, MathF.Max(pivotCoord, newCoord));
            solved = new BoundingBox3D(min, max);
        }

        bool modified = solved.Min != bounds.Min || solved.Max != bounds.Max;
        bounds = solved;
        return modified;
    }

    /// <summary>
    /// Returns a world-space corner of one box face. <paramref name="cornerIndex"/>
    /// follows the reference winding: along the face axes the coordinates are
    /// (min, min), (min, max), (max, max), (max, min) for indices 0-3.
    /// </summary>
    public static Vector3 BoundsFaceCorner(in BoundingBox3D bounds, int axis, bool faceMax, int cornerIndex)
    {
        int secondAxis = (axis + 1) % 3;
        int thirdAxis = (axis + 2) % 3;
        float faceCoord = faceMax ? GizmoMath.Component(bounds.Max, axis) : GizmoMath.Component(bounds.Min, axis);
        float secondCoord = (cornerIndex >> 1) == 0
            ? GizmoMath.Component(bounds.Min, secondAxis)
            : GizmoMath.Component(bounds.Max, secondAxis);
        float thirdCoord = ((cornerIndex >> 1) ^ (cornerIndex & 1)) == 0
            ? GizmoMath.Component(bounds.Min, thirdAxis)
            : GizmoMath.Component(bounds.Max, thirdAxis);

        Vector3 corner = Vector3.Zero;
        corner = GizmoMath.WithComponent(corner, axis, faceCoord);
        corner = GizmoMath.WithComponent(corner, secondAxis, secondCoord);
        corner = GizmoMath.WithComponent(corner, thirdAxis, thirdCoord);
        return corner;
    }

    /// <summary>Activates, solves and releases rotation drags.</summary>
    private static bool HandleRotation(GizmoContext ctx, ref Matrix4x4 matrix, ref Matrix4x4 deltaMatrix,
        GizmoOperation op, ref GizmoMoveType type, GizmoSnap? snap)
    {
        bool mouseOver = ctx.Viewport.Contains(ctx.Input.MousePos) || ctx.Using;
        if (!Intersects(op, GizmoOperation.Rotate | GizmoOperation.RotateScreen) || type != GizmoMoveType.None || !mouseOver)
        {
            return false;
        }

        bool applyRotationLocally = ctx.Mode == GizmoMode.Local;
        bool modified = false;
        Vector3 modelPos = GizmoMath.Translation(ctx.Model);

        if (!ctx.Using)
        {
            type = ctx.OverGizmoHotspot ? GizmoMoveType.None : GetRotateType(ctx, op);
            ctx.OverGizmoHotspot |= type != GizmoMoveType.None;
            if (type == GizmoMoveType.RotateScreen)
            {
                applyRotationLocally = true;
            }

            if (CanActivate(ctx) && type != GizmoMoveType.None)
            {
                ctx.Using = true;
                ctx.CurrentOperation = type;
                Span<Vector3> rotatePlanNormal = stackalloc Vector3[4]
                {
                    GizmoMath.AxisRow(ctx.Model, 0), GizmoMath.AxisRow(ctx.Model, 1), GizmoMath.AxisRow(ctx.Model, 2), -ctx.CameraDir,
                };
                ctx.TranslationPlan = applyRotationLocally
                    ? GizmoMath.BuildPlan(modelPos, rotatePlanNormal[type - GizmoMoveType.RotateX])
                    : GizmoMath.BuildPlan(GizmoMath.Translation(ctx.ModelSource), GizmoMath.DirectionUnary[type - GizmoMoveType.RotateX]);

                float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, ctx.TranslationPlan);
                Vector3 localPos = ctx.RayOrigin + ctx.RayVector * len - modelPos;
                ctx.RotationVectorSource = GizmoMath.NormalizeSafe(localPos);
                ctx.RotationAngleOrigin = ComputeAngleOnPlan(ctx);
            }
        }

        if (ctx.Using && IsRotateType(ctx.CurrentOperation))
        {
            ctx.RotationAngle = ComputeAngleOnPlan(ctx);
            if (snap.HasValue)
            {
                float snapInRadian = snap.Value.RotationDegrees * (MathF.PI / 180f);
                float angle = ctx.RotationAngle;
                GizmoMath.ComputeSnap(ref angle, snapInRadian);
                ctx.RotationAngle = angle;
            }

            Vector3 planNormal = new Vector3(ctx.TranslationPlan.X, ctx.TranslationPlan.Y, ctx.TranslationPlan.Z);
            Vector3 rotationAxisLocalSpace = Vector3.TransformNormal(planNormal, ctx.ModelInverse);
            rotationAxisLocalSpace = GizmoMath.NormalizeSafe(rotationAxisLocalSpace);

            Matrix4x4 deltaRotation = Matrix4x4.CreateFromAxisAngle(rotationAxisLocalSpace, ctx.RotationAngle - ctx.RotationAngleOrigin);
            if (ctx.RotationAngle != ctx.RotationAngleOrigin)
            {
                modified = true;
            }

            ctx.RotationAngleOrigin = ctx.RotationAngle;

            if (applyRotationLocally)
            {
                matrix = Matrix4x4.CreateScale(ctx.ModelScaleOrigin) * deltaRotation * ctx.ModelLocal;
            }
            else
            {
                Matrix4x4 res = ctx.ModelSource;
                res.M41 = 0f;
                res.M42 = 0f;
                res.M43 = 0f;
                matrix = res * deltaRotation;
                Vector3 sourcePos = GizmoMath.Translation(ctx.ModelSource);
                matrix.M41 = sourcePos.X;
                matrix.M42 = sourcePos.Y;
                matrix.M43 = sourcePos.Z;
            }

            deltaMatrix = ctx.ModelInverse * deltaRotation * ctx.Model;

            if (!ctx.Input.MouseDown)
            {
                ctx.Using = false;
            }

            type = ctx.CurrentOperation;
        }

        return modified;
    }

    /// <summary>
    /// Measures the signed angle between the drag-start direction and the current
    /// ray hit on the rotation plane.
    /// </summary>
    private static float ComputeAngleOnPlan(GizmoContext ctx)
    {
        float len = GizmoMath.IntersectRayPlane(ctx.RayOrigin, ctx.RayVector, ctx.TranslationPlan);
        Vector3 modelPos = GizmoMath.Translation(ctx.Model);
        Vector3 localPos = GizmoMath.NormalizeSafe(ctx.RayOrigin + ctx.RayVector * len - modelPos);

        Vector3 planNormal = new Vector3(ctx.TranslationPlan.X, ctx.TranslationPlan.Y, ctx.TranslationPlan.Z);
        Vector3 perpendicularVector = Vector3.Cross(ctx.RotationVectorSource, planNormal);
        perpendicularVector = GizmoMath.NormalizeSafe(perpendicularVector);
        float acosAngle = Math.Clamp(Vector3.Dot(localPos, ctx.RotationVectorSource), -1f, 1f);
        float angle = MathF.Acos(acosAngle);
        angle *= Vector3.Dot(localPos, perpendicularVector) < 0f ? 1f : -1f;
        return angle;
    }

    private static void SetComponent(ref Vector3 v, int index, float value)
    {
        if (index == 0)
        {
            v.X = value;
        }
        else if (index == 1)
        {
            v.Y = value;
        }
        else
        {
            v.Z = value;
        }
    }
}
