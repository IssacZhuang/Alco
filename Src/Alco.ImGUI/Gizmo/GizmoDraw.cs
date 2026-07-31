using System.Numerics;

namespace Alco.ImGUI;

/// <summary>
/// The gizmo draw layer: converts the solved per-call state of a
/// <see cref="GizmoContext"/> into ImDrawList primitives on the current window's
/// draw list. This is the only gizmo file that touches ImGui; all solving has
/// already happened in <see cref="GizmoCore"/>.
/// </summary>
internal static class GizmoDraw
{
    /// <summary>Segments per half circle when tessellating rotation rings.</summary>
    private const int HalfCircleSegmentCount = 64;

    /// <summary>Scale factor for the rotation rings relative to the translation handles.</summary>
    private const float RotationDisplayFactor = 1.2f;

    /// <summary>Base radius of the screen-space rotation ring as a fraction of the viewport height.</summary>
    private const float ScreenRotateSize = 0.06f;

    /// <summary>Plane handle quad UVs (min/max extent in gizmo-sized units).</summary>
    private static readonly float[] QuadUV = { 0.5f, 0.5f, 0.5f, 0.8f, 0.8f, 0.8f, 0.8f, 0.5f };

    /// <summary>Component indices used with the translation/scale drag info text.</summary>
    private static readonly int[] TranslationInfoIndex = { 0, 0, 0, 1, 0, 0, 2, 0, 0, 1, 2, 0, 0, 2, 0, 0, 1, 0, 0, 1, 2 };

    /// <summary>Info text labels for rotation drags, indexed by handle.</summary>
    private static readonly string[] RotationInfoLabel = { "X", "Y", "Z", "Screen" };

    /// <summary>White, for inactive center handles.</summary>
    private const uint ColorWhite = 0xFFFFFFFF;

    /// <summary>
    /// Draws all enabled gizmo handles for the current call state, clipped to the viewport.
    /// </summary>
    /// <param name="ctx">The gizmo context with a valid per-call working set.</param>
    /// <param name="drawList">The draw list of the current ImGui window.</param>
    public static void Draw(GizmoContext ctx, ImDrawListPtr drawList)
    {
        drawList.PushClipRect(ctx.Viewport.Min, ctx.Viewport.Max, false);
        DrawRotationGizmo(ctx, drawList, ctx.Operation, ctx.CallType);
        DrawTranslationGizmo(ctx, drawList, ctx.Operation, ctx.CallType);
        DrawScaleGizmo(ctx, drawList, ctx.Operation, ctx.CallType);
        drawList.PopClipRect();
    }

    /// <summary>
    /// Draws a grid on the model-local XY plane (Z = 0, the Alco ground plane),
    /// clipped against the camera frustum.
    /// </summary>
    /// <param name="viewport">The viewport in pixels.</param>
    /// <param name="view">The camera view matrix.</param>
    /// <param name="projection">The camera projection matrix.</param>
    /// <param name="model">The model matrix transforming the grid's local XY plane.</param>
    /// <param name="gridSize">Half extent of the grid in local units.</param>
    /// <param name="drawList">The draw list of the current ImGui window.</param>
    public static void DrawGrid(Rect viewport, in Matrix4x4 view, in Matrix4x4 projection, in Matrix4x4 model, float gridSize, ImDrawListPtr drawList)
    {
        Matrix4x4 viewProjection = view * projection;
        Span<Vector4> frustum = stackalloc Vector4[6];
        GizmoMath.ComputeFrustumPlanes(viewProjection, frustum);
        Matrix4x4 res = model * viewProjection;

        for (float f = -gridSize; f <= gridSize; f += 1f)
        {
            for (int dir = 0; dir < 2; dir++)
            {
                Vector3 ptA = new Vector3(dir != 0 ? -gridSize : f, dir != 0 ? f : -gridSize, 0f);
                Vector3 ptB = new Vector3(dir != 0 ? gridSize : f, dir != 0 ? f : gridSize, 0f);
                bool visible = true;
                for (int i = 0; i < 6; i++)
                {
                    float dA = GizmoMath.DistanceToPlane(ptA, frustum[i]);
                    float dB = GizmoMath.DistanceToPlane(ptB, frustum[i]);
                    if (dA < 0f && dB < 0f)
                    {
                        visible = false;
                        break;
                    }

                    if (dA > 0f && dB > 0f)
                    {
                        continue;
                    }

                    if (dA < 0f)
                    {
                        float len = MathF.Abs(dA - dB);
                        float t = MathF.Abs(dA) / len;
                        ptA = Vector3.Lerp(ptA, ptB, t);
                    }

                    if (dB < 0f)
                    {
                        float len = MathF.Abs(dB - dA);
                        float t = MathF.Abs(dB) / len;
                        ptB = Vector3.Lerp(ptA, ptB, t);
                    }
                }

                if (!visible)
                {
                    continue;
                }

                uint col = 0xFF808080;
                col = MathF.Abs(f) % 10f < GizmoMath.Epsilon ? 0xFF909090 : col;
                col = MathF.Abs(f) < GizmoMath.Epsilon ? 0xFF404040 : col;

                float thickness = 1f;
                thickness = MathF.Abs(f) % 10f < GizmoMath.Epsilon ? 1.5f : thickness;
                thickness = MathF.Abs(f) < GizmoMath.Epsilon ? 2.3f : thickness;

                drawList.AddLine(
                    GizmoMath.WorldToScreen(ptA, res, viewport),
                    GizmoMath.WorldToScreen(ptB, res, viewport),
                    col, thickness);
            }
        }
    }

    /// <summary>Computes the per-handle colors for one gizmo category from hover/active state.</summary>
    private static void ComputeColors(GizmoContext ctx, Span<uint> colors, GizmoMoveType type, GizmoOperation category)
    {
        GizmoStyle style = ctx.Style;
        uint selectionColor = style.SelectionColor;
        Span<uint> directionColors = stackalloc uint[3] { style.DirectionXColor, style.DirectionYColor, style.DirectionZColor };
        Span<uint> planeColors = stackalloc uint[3] { style.PlaneXColor, style.PlaneYColor, style.PlaneZColor };

        if (category == GizmoOperation.Translate)
        {
            colors[0] = type == GizmoMoveType.MoveScreen ? selectionColor : ColorWhite;
            for (int i = 0; i < 3; i++)
            {
                colors[i + 1] = type == GizmoMoveType.MoveX + i ? selectionColor : directionColors[i];
                colors[i + 4] = type == GizmoMoveType.MoveYZ + i ? selectionColor : planeColors[i];
                colors[i + 4] = type == GizmoMoveType.MoveScreen ? selectionColor : colors[i + 4];
            }
        }
        else if (category == GizmoOperation.Rotate)
        {
            colors[0] = type == GizmoMoveType.RotateScreen ? selectionColor : ColorWhite;
            for (int i = 0; i < 3; i++)
            {
                colors[i + 1] = type == GizmoMoveType.RotateX + i ? selectionColor : directionColors[i];
            }
        }
        else
        {
            colors[0] = type == GizmoMoveType.ScaleXYZ ? selectionColor : ColorWhite;
            for (int i = 0; i < 3; i++)
            {
                colors[i + 1] = type == GizmoMoveType.ScaleX + i ? selectionColor : directionColors[i];
            }
        }
    }

    /// <summary>Draws the rotation rings and the active rotation sector/info.</summary>
    private static void DrawRotationGizmo(GizmoContext ctx, ImDrawListPtr drawList, GizmoOperation op, GizmoMoveType type)
    {
        if ((op & (GizmoOperation.Rotate | GizmoOperation.RotateScreen)) == 0)
        {
            return;
        }

        Span<uint> colors = stackalloc uint[7];
        ComputeColors(ctx, colors, type, GizmoOperation.Rotate);

        Vector3 modelPos = GizmoMath.Translation(ctx.Model);
        Vector3 cameraToModelNormalized = ctx.IsOrthographic
            ? -ctx.CameraDir
            : GizmoMath.NormalizeSafe(modelPos - ctx.CameraEye);
        cameraToModelNormalized = Vector3.TransformNormal(cameraToModelNormalized, ctx.ModelInverse);

        ctx.RadiusSquareCenter = ScreenRotateSize * ctx.Viewport.Size.Y;

        bool hasRSC = (op & GizmoOperation.RotateScreen) != 0;
        Span<Vector2> ringBuffer = stackalloc Vector2[2 * HalfCircleSegmentCount + 1];
        for (int axis = 0; axis < 3; axis++)
        {
            if ((op & (GizmoOperation)((int)GizmoOperation.RotateZ >> axis)) == 0)
            {
                continue;
            }

            bool usingAxis = ctx.Using && type == GizmoMoveType.RotateZ - axis;
            int circleMul = hasRSC && !usingAxis ? 1 : 2;
            int pointCount = circleMul * HalfCircleSegmentCount + 1;
            Span<Vector2> circlePos = ringBuffer[..pointCount];

            float angleStart = MathF.Atan2(
                GizmoMath.Component(cameraToModelNormalized, (4 - axis) % 3),
                GizmoMath.Component(cameraToModelNormalized, (3 - axis) % 3)) + MathF.PI * 0.5f;

            for (int i = 0; i < pointCount; i++)
            {
                float ng = angleStart + circleMul * MathF.PI * (i / (float)(pointCount - 1));
                Vector3 axisPos = new Vector3(MathF.Cos(ng), MathF.Sin(ng), 0f);
                Vector3 pos = new Vector3(
                    GizmoMath.Component(axisPos, axis),
                    GizmoMath.Component(axisPos, (axis + 1) % 3),
                    GizmoMath.Component(axisPos, (axis + 2) % 3)) * (ctx.ScreenFactor * RotationDisplayFactor);
                circlePos[i] = GizmoMath.WorldToScreen(pos, ctx.Mvp, ctx.Viewport);
            }

            if (!ctx.Using || usingAxis)
            {
                drawList.AddPolyline(ref circlePos[0], pointCount, colors[3 - axis], ImDrawFlags.None, ctx.Style.RotationLineThickness);
            }

            float radiusAxis = (GizmoMath.WorldToScreen(modelPos, ctx.ViewProjection, ctx.Viewport) - circlePos[0]).Length();
            if (radiusAxis > ctx.RadiusSquareCenter)
            {
                ctx.RadiusSquareCenter = radiusAxis;
            }
        }

        if (hasRSC && (!ctx.Using || type == GizmoMoveType.RotateScreen))
        {
            drawList.AddCircle(GizmoMath.WorldToScreen(modelPos, ctx.ViewProjection, ctx.Viewport),
                ctx.RadiusSquareCenter, colors[0], 64, ctx.Style.RotationOuterLineThickness);
        }

        if (ctx.Using && type >= GizmoMoveType.RotateX && type <= GizmoMoveType.RotateScreen)
        {
            Span<Vector2> circlePos = stackalloc Vector2[HalfCircleSegmentCount + 1];
            circlePos[0] = GizmoMath.WorldToScreen(modelPos, ctx.ViewProjection, ctx.Viewport);
            Vector3 planNormal = new Vector3(ctx.TranslationPlan.X, ctx.TranslationPlan.Y, ctx.TranslationPlan.Z);
            for (int i = 1; i < HalfCircleSegmentCount + 1; i++)
            {
                float ng = ctx.RotationAngle * ((i - 1) / (float)(HalfCircleSegmentCount - 1));
                Matrix4x4 rotateVectorMatrix = Matrix4x4.CreateFromAxisAngle(planNormal, ng);
                Vector3 pos = Vector3.Transform(ctx.RotationVectorSource, rotateVectorMatrix)
                    * (ctx.ScreenFactor * RotationDisplayFactor);
                circlePos[i] = GizmoMath.WorldToScreen(pos + modelPos, ctx.ViewProjection, ctx.Viewport);
            }

            drawList.AddConvexPolyFilled(ref circlePos[0], HalfCircleSegmentCount + 1, ctx.Style.RotationUsingFillColor);
            drawList.AddPolyline(ref circlePos[0], HalfCircleSegmentCount + 1, ctx.Style.RotationUsingBorderColor, ImDrawFlags.Closed, ctx.Style.RotationLineThickness);

            Vector2 destinationPosOnScreen = circlePos[1];
            float displayAngle = ctx.RotationAngle * EngineRotationSign(type);
            FixedString64 text = RotationInfoLabel[type - GizmoMoveType.RotateX];
            text.Append(" : ");
            text.Append(displayAngle / MathF.PI * 180f, 2);
            text.Append(" deg ");
            text.Append(displayAngle, 2);
            text.Append(" rad");
            DrawInfoText(drawList, ctx, destinationPosOnScreen, text);
        }
    }

    /// <summary>
    /// Sign converting the raw right-handed plane angle to the engine's euler convention
    /// (Roll X / Pitch Y are RH-negative, Yaw Z is RH-positive), so the rotation info text
    /// matches the inspector's euler readout (<see cref="math.euler"/>).
    /// </summary>
    internal static float EngineRotationSign(GizmoMoveType type)
    {
        return type is GizmoMoveType.RotateX or GizmoMoveType.RotateY ? -1f : 1f;
    }

    /// <summary>Draws the translation axes, plane quads, center handle and active drag info.</summary>
    private static void DrawTranslationGizmo(GizmoContext ctx, ImDrawListPtr drawList, GizmoOperation op, GizmoMoveType type)
    {
        if ((op & GizmoOperation.Translate) == 0)
        {
            return;
        }

        Span<uint> colors = stackalloc uint[7];
        ComputeColors(ctx, colors, type, GizmoOperation.Translate);

        Vector3 modelPos = GizmoMath.Translation(ctx.Model);
        Vector2 origin = GizmoMath.WorldToScreen(modelPos, ctx.ViewProjection, ctx.Viewport);
        Span<uint> directionColors = stackalloc uint[3] { ctx.Style.DirectionXColor, ctx.Style.DirectionYColor, ctx.Style.DirectionZColor };
        Span<Vector2> screenQuadPts = stackalloc Vector2[4];

        for (int i = 0; i < 3; i++)
        {
            GizmoCore.ComputeTripodAxisAndVisibility(ctx, i,
                out Vector3 dirAxis, out Vector3 dirPlaneX, out Vector3 dirPlaneY,
                out bool belowAxisLimit, out bool belowPlaneLimit, false);

            if ((!ctx.Using || type == GizmoMoveType.MoveX + i)
                && belowAxisLimit
                && (op & (GizmoOperation)((int)GizmoOperation.TranslateX << i)) != 0)
            {
                Vector2 baseSSpace = GizmoMath.WorldToScreen(dirAxis * (0.1f * ctx.ScreenFactor), ctx.Mvp, ctx.Viewport);
                Vector2 worldDirSSpace = GizmoMath.WorldToScreen(dirAxis * ctx.ScreenFactor, ctx.Mvp, ctx.Viewport);

                drawList.AddLine(baseSSpace, worldDirSSpace, colors[i + 1], ctx.Style.TranslationLineThickness);

                // Arrow head.
                Vector2 dir = origin - worldDirSSpace;
                float d = dir.Length();
                if (d > GizmoMath.Epsilon)
                {
                    dir = dir / d * ctx.Style.TranslationLineArrowSize;
                    Vector2 ortogonalDir = new Vector2(dir.Y, -dir.X);
                    Vector2 a = worldDirSSpace + dir;
                    drawList.AddTriangleFilled(worldDirSSpace - dir, a + ortogonalDir, a - ortogonalDir, colors[i + 1]);
                }

                if (ctx.AxisFactor[i] < 0f)
                {
                    DrawHatchedAxis(ctx, drawList, dirAxis);
                }
            }

            if ((!ctx.Using || type == GizmoMoveType.MoveYZ + i)
                && belowPlaneLimit
                && (op & GizmoCore.TranslatePlans[i]) == GizmoCore.TranslatePlans[i])
            {
                for (int j = 0; j < 4; j++)
                {
                    Vector3 cornerWorldPos = (dirPlaneX * QuadUV[j * 2] + dirPlaneY * QuadUV[j * 2 + 1]) * ctx.ScreenFactor;
                    screenQuadPts[j] = GizmoMath.WorldToScreen(cornerWorldPos, ctx.Mvp, ctx.Viewport);
                }

                drawList.AddPolyline(ref screenQuadPts[0], 4, directionColors[i], ImDrawFlags.Closed, 1.0f);
                drawList.AddConvexPolyFilled(ref screenQuadPts[0], 4, colors[i + 4]);
            }
        }

        drawList.AddCircleFilled(ctx.ScreenSquareCenter, ctx.Style.CenterCircleSize, colors[0], 32);

        if (ctx.Using && type >= GizmoMoveType.MoveX && type <= GizmoMoveType.MoveScreen)
        {
            Vector2 sourcePosOnScreen = GizmoMath.WorldToScreen(ctx.MatrixOrigin, ctx.ViewProjection, ctx.Viewport);
            Vector2 destinationPosOnScreen = GizmoMath.WorldToScreen(modelPos, ctx.ViewProjection, ctx.Viewport);
            Vector2 dif = destinationPosOnScreen - sourcePosOnScreen;
            float difLength = dif.Length();
            if (difLength > GizmoMath.Epsilon)
            {
                dif = dif / difLength * 5f;
                drawList.AddCircle(sourcePosOnScreen, 6f, ctx.Style.TranslationLineColor);
                drawList.AddCircle(destinationPosOnScreen, 6f, ctx.Style.TranslationLineColor);
                drawList.AddLine(
                    new Vector2(sourcePosOnScreen.X + dif.X, sourcePosOnScreen.Y + dif.Y),
                    new Vector2(destinationPosOnScreen.X - dif.X, destinationPosOnScreen.Y - dif.Y),
                    ctx.Style.TranslationLineColor, 2f);
            }

            // Show the drag delta in the caller's authoring unit (InfoUnitScale), not raw world units.
            Vector3 deltaInfo = (modelPos - ctx.MatrixOrigin) * ctx.InfoUnitScale;
            int maskIndex = type - GizmoMoveType.MoveX;
            int componentInfoIndex = maskIndex * 3;
            int componentCount = maskIndex < 3 ? 1 : maskIndex < 6 ? 2 : 3;
            FixedString64 text = new();
            for (int k = 0; k < componentCount; k++)
            {
                if (k > 0)
                {
                    text.Append(' ');
                }
                int component = TranslationInfoIndex[componentInfoIndex + k];
                text.Append("XYZ"[component]);
                text.Append(" : ");
                text.Append(GizmoMath.Component(deltaInfo, component), 3);
            }
            DrawInfoText(drawList, ctx, destinationPosOnScreen, text);
        }
    }

    /// <summary>Draws the scale axes, center handle, uniform ring and active drag info.</summary>
    private static void DrawScaleGizmo(GizmoContext ctx, ImDrawListPtr drawList, GizmoOperation op, GizmoMoveType type)
    {
        if ((op & (GizmoOperation.Scale | GizmoOperation.ScaleUniform)) == 0)
        {
            return;
        }

        Span<uint> colors = stackalloc uint[7];
        ComputeColors(ctx, colors, type, GizmoOperation.Scale);

        Vector3 scaleDisplay = Vector3.One;
        if (ctx.Using)
        {
            scaleDisplay = ctx.Scale;
        }

        for (int i = 0; i < 3; i++)
        {
            if ((op & (GizmoOperation)((int)GizmoOperation.ScaleX << i)) == 0)
            {
                continue;
            }

            bool usingAxis = ctx.Using && type == GizmoMoveType.ScaleX + i;
            if (ctx.Using && !usingAxis)
            {
                continue;
            }

            GizmoCore.ComputeTripodAxisAndVisibility(ctx, i,
                out Vector3 dirAxis, out _, out _,
                out bool belowAxisLimit, out _, true);

            if (!belowAxisLimit)
            {
                continue;
            }

            bool hasTranslateOnAxis = (op & (GizmoOperation)((int)GizmoOperation.TranslateX << i)) != 0;
            float markerScale = hasTranslateOnAxis ? 1.4f : 1.0f;
            Vector2 baseSSpace = GizmoMath.WorldToScreen(dirAxis * (0.1f * ctx.ScreenFactor), ctx.Mvp, ctx.Viewport);
            Vector2 worldDirSSpaceNoScale = GizmoMath.WorldToScreen(dirAxis * (markerScale * ctx.ScreenFactor), ctx.Mvp, ctx.Viewport);
            Vector2 worldDirSSpace = GizmoMath.WorldToScreen(
                dirAxis * (markerScale * GizmoMath.Component(scaleDisplay, i)) * ctx.ScreenFactor, ctx.Mvp, ctx.Viewport);

            if (ctx.Using)
            {
                drawList.AddLine(baseSSpace, worldDirSSpaceNoScale, ctx.Style.ScaleLineColor, ctx.Style.ScaleLineThickness);
                drawList.AddCircleFilled(worldDirSSpaceNoScale, ctx.Style.ScaleLineCircleSize, ctx.Style.ScaleLineColor);
            }

            if (!hasTranslateOnAxis || ctx.Using)
            {
                drawList.AddLine(baseSSpace, worldDirSSpace, colors[i + 1], ctx.Style.ScaleLineThickness);
            }

            drawList.AddCircleFilled(worldDirSSpace, ctx.Style.ScaleLineCircleSize, colors[i + 1]);

            if (ctx.AxisFactor[i] < 0f)
            {
                DrawHatchedAxis(ctx, drawList, dirAxis * GizmoMath.Component(scaleDisplay, i));
            }
        }

        drawList.AddCircleFilled(ctx.ScreenSquareCenter, ctx.Style.CenterCircleSize, colors[0], 32);

        if ((op & GizmoOperation.ScaleUniform) != 0)
        {
            drawList.AddCircle(ctx.ScreenSquareCenter, 20f, colors[0], 32, ctx.Style.CenterCircleSize);
        }

        if (ctx.Using && type >= GizmoMoveType.ScaleX && type <= GizmoMoveType.ScaleXYZ)
        {
            Vector2 destinationPosOnScreen = GizmoMath.WorldToScreen(GizmoMath.Translation(ctx.Model), ctx.ViewProjection, ctx.Viewport);
            int maskIndex = type - GizmoMoveType.ScaleX;
            int componentInfoIndex = maskIndex * 3;
            float value = GizmoMath.Component(scaleDisplay, TranslationInfoIndex[componentInfoIndex]);
            FixedString64 text = new();
            if (maskIndex < 3)
            {
                text.Append("XYZ"[maskIndex]);
            }
            else
            {
                text.Append("XYZ");
            }
            text.Append(" : ");
            text.Append(value, 2);
            DrawInfoText(drawList, ctx, destinationPosOnScreen, text);
        }
    }

    /// <summary>Draws hatch marks along a flipped axis.</summary>
    private static void DrawHatchedAxis(GizmoContext ctx, ImDrawListPtr drawList, in Vector3 axis)
    {
        if (ctx.Style.HatchedAxisLineThickness <= 0.0f)
        {
            return;
        }

        for (int j = 1; j < 10; j++)
        {
            Vector2 baseSSpace = GizmoMath.WorldToScreen(axis * (0.05f * (j * 2)) * ctx.ScreenFactor, ctx.Mvp, ctx.Viewport);
            Vector2 worldDirSSpace = GizmoMath.WorldToScreen(axis * (0.05f * (j * 2 + 1)) * ctx.ScreenFactor, ctx.Mvp, ctx.Viewport);
            drawList.AddLine(baseSSpace, worldDirSSpace, ctx.Style.HatchedAxisLinesColor, ctx.Style.HatchedAxisLineThickness);
        }
    }

    /// <summary>Draws the drag info text with a shadow offset, ImGuizmo style.</summary>
    private static void DrawInfoText(ImDrawListPtr drawList, GizmoContext ctx, Vector2 position, ReadOnlySpan<char> text)
    {
        drawList.AddText(new Vector2(position.X + 15f, position.Y + 15f), ctx.Style.TextShadowColor, text);
        drawList.AddText(new Vector2(position.X + 14f, position.Y + 14f), ctx.Style.TextColor, text);
    }
}
