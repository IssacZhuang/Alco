using System.Numerics;
using NanoSvgSharp;

namespace Alco.Rendering;

/// <summary>GPU rasterization payload built from a parsed SVG image: a flat edge list
/// of line segments grouped by paint shape, plus per-shape paints (solid colors or
/// baked gradient ramps). All coordinates are device pixels, y-down.</summary>
public sealed class SvgRasterizeData
{
    /// <summary>Number of ramp entries baked per gradient shape.</summary>
    public const int GradientRampSize = 64;

    /// <summary>Paint kind encoded in <see cref="ShapeMeta"/>.W: solid color.</summary>
    public const uint PaintSolid = 0;

    /// <summary>Paint kind encoded in <see cref="ShapeMeta"/>.W: linear gradient.</summary>
    public const uint PaintLinearGradient = 1;

    /// <summary>Paint kind encoded in <see cref="ShapeMeta"/>.W: radial gradient.</summary>
    public const uint PaintRadialGradient = 2;

    /// <summary>Output texture width in pixels.</summary>
    public uint Width { get; init; }

    /// <summary>Output texture height in pixels.</summary>
    public uint Height { get; init; }

    /// <summary>Line segments as (x0, y0, x1, y1).</summary>
    public List<Vector4> Edges { get; } = [];

    /// <summary>Culling bounding box per shape as (minx, miny, maxx, maxy), dilated by
    /// one pixel so antialiased coverage at the border is not clipped.</summary>
    public List<Vector4> ShapeBboxes { get; } = [];

    /// <summary>Per shape: (edgeStart, edgeCount, fillRule, paintInfo). paintInfo packs
    /// the paint kind (bits 0-1), the gradient spread (bits 2-3) and the gradient ramp
    /// start entry (bits 4+).</summary>
    public List<uint4> ShapeMeta { get; } = [];

    /// <summary>Straight (non-premultiplied) RGBA color per solid-paint shape.</summary>
    public List<Vector4> ShapeColors { get; } = [];

    /// <summary>Affine device-pixel-to-gradient-space transforms, two entries per
    /// gradient shape: [a, b, c, d] and [e, f, focalX, focalY] (row-major 2x3).</summary>
    public List<Vector4> GradientXforms { get; } = [];

    /// <summary>Baked color ramps, <see cref="GradientRampSize"/> straight RGBA entries
    /// per gradient shape, sampled over t in [0, 1].</summary>
    public List<Vector4> GradientRamps { get; } = [];
}

/// <summary>Turns a parsed <see cref="NanoSvgImage"/> into <see cref="SvgRasterizeData"/>:
/// cubic-bezier flattening, dash application and stroke-to-outline expansion run on the
/// CPU; coverage evaluation happens on the GPU. Pure functions, no GPU resources.</summary>
public static class SvgGeometryUtility
{
    private const int MaxRecursionDepth = 16;

    /// <summary>Builds rasterization data for an image rendered into a target-size texture.
    /// The image is uniformly scaled to fit the target and centered, mirroring the SVG
    /// default preserveAspectRatio meet rule.</summary>
    /// <param name="image">The parsed image (geometry in image pixel space).</param>
    /// <param name="targetWidth">Output texture width in pixels.</param>
    /// <param name="targetHeight">Output texture height in pixels.</param>
    /// <param name="tolerance">Curve flattening tolerance in device pixels.</param>
    /// <returns>The rasterization payload.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The target size is zero.</exception>
    public static SvgRasterizeData Build(NanoSvgImage image, uint targetWidth, uint targetHeight, float tolerance = 0.15f)
    {
        if (targetWidth == 0 || targetHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "The target size must not be zero.");
        }

        SvgRasterizeData data = new SvgRasterizeData
        {
            Width = targetWidth,
            Height = targetHeight,
        };

        float scale = FitScale(image.Width, image.Height, targetWidth, targetHeight);
        float offsetX = (targetWidth - image.Width * scale) * 0.5f;
        float offsetY = (targetHeight - image.Height * scale) * 0.5f;

        foreach (NanoSvgShape shape in image.Shapes)
        {
            if ((shape.Flags & NanoSvgShapeFlags.Visible) == 0 || shape.Opacity <= 0)
            {
                continue;
            }

            // Paint order slots: 0 = fill, 1 = markers (unsupported), 2 = stroke.
            for (int slot = 0; slot < 3; slot++)
            {
                int kind = (shape.PaintOrder >> (slot * 2)) & 0x3;
                if (kind == 0)
                {
                    EmitFill(data, shape, scale, offsetX, offsetY, tolerance);
                }
                else if (kind == 2)
                {
                    EmitStroke(data, shape, scale, offsetX, offsetY, tolerance);
                }
            }
        }

        return data;
    }

    private static float FitScale(float width, float height, uint targetWidth, uint targetHeight)
    {
        if (width <= 0 || height <= 0)
        {
            return 1.0f;
        }
        return MathF.Min(targetWidth / width, targetHeight / height);
    }

    private static void EmitFill(SvgRasterizeData data, NanoSvgShape shape, float scale, float offsetX, float offsetY, float tolerance)
    {
        if (shape.Fill.Type == NanoSvgPaintType.None)
        {
            return;
        }

        int shapeIndex = BeginShape(data, shape.Fill, shape.Opacity, scale, offsetX, offsetY, out uint paintInfo);
        if (shapeIndex < 0)
        {
            return;
        }

        uint fillRule = shape.FillRule == NanoSvgFillRule.EvenOdd ? 1u : 0u;
        int edgeStart = data.Edges.Count;
        foreach (NanoSvgPath path in shape.Paths)
        {
            Contour contour = FlattenContour(path, scale, offsetX, offsetY, tolerance);
            AddContourEdges(data, contour);
        }

        if (!EndShape(data, shapeIndex, edgeStart, fillRule, paintInfo))
        {
            RemoveEmptyShape(data, shapeIndex);
        }
    }

    private static void EmitStroke(SvgRasterizeData data, NanoSvgShape shape, float scale, float offsetX, float offsetY, float tolerance)
    {
        if (shape.Stroke.Type == NanoSvgPaintType.None || shape.StrokeWidth <= 0)
        {
            return;
        }

        int shapeIndex = BeginShape(data, shape.Stroke, shape.Opacity, scale, offsetX, offsetY, out uint paintInfo);
        if (shapeIndex < 0)
        {
            return;
        }

        float halfWidth = shape.StrokeWidth * scale * 0.5f;
        float[] dashArray = ScaleDashArray(shape, scale);
        float dashOffset = shape.StrokeDashOffset * scale;

        int edgeStart = data.Edges.Count;
        List<Vector2> sink = [];
        foreach (NanoSvgPath path in shape.Paths)
        {
            Contour contour = FlattenContour(path, scale, offsetX, offsetY, tolerance);
            if (dashArray.Length > 0)
            {
                foreach (Contour dashed in ApplyDashes(contour, dashArray, dashOffset))
                {
                    sink.Clear();
                    ExpandStroke(dashed.Points, dashed.Closed, halfWidth, shape.StrokeLineCap, shape.StrokeLineJoin, shape.MiterLimit, sink, data);
                }
            }
            else
            {
                sink.Clear();
                ExpandStroke(contour.Points, contour.Closed, halfWidth, shape.StrokeLineCap, shape.StrokeLineJoin, shape.MiterLimit, sink, data);
            }
        }

        if (!EndShape(data, shapeIndex, edgeStart, 0u, paintInfo))
        {
            RemoveEmptyShape(data, shapeIndex);
        }
    }

    /// <summary>Reserves a paint slot; returns its index, or -1 when the paint is
    /// effectively invisible (fully transparent or a missing gradient).</summary>
    private static int BeginShape(SvgRasterizeData data, NanoSvgPaint paint, float opacity, float scale, float offsetX, float offsetY, out uint paintInfo)
    {
        paintInfo = 0;
        Vector4 solidColor = default;

        switch (paint.Type)
        {
            case NanoSvgPaintType.Color:
            {
                Vector4 c = UnpackColor(paint.Color);
                solidColor = new Vector4(c.X, c.Y, c.Z, c.W * opacity);
                if (solidColor.W <= 0)
                {
                    return -1;
                }
                paintInfo = SvgRasterizeData.PaintSolid;
                break;
            }
            case NanoSvgPaintType.LinearGradient:
            case NanoSvgPaintType.RadialGradient:
            {
                if (paint.Gradient == null || paint.Gradient.Stops.Count == 0)
                {
                    return -1;
                }
                // The parser's xform maps image pixels to gradient space; compose the
                // device-to-image transform behind it so it maps device pixels directly.
                float[] m = (float[])paint.Gradient.Xform.Clone();
                float[] toImage = [1.0f / scale, 0, 0, 1.0f / scale, -offsetX / scale, -offsetY / scale];
                MultiplyXform(m, toImage);

                int rampStart = BakeGradientRamp(data, paint.Gradient, opacity);
                data.GradientXforms.Add(new Vector4(m[0], m[1], m[2], m[3]));
                data.GradientXforms.Add(new Vector4(m[4], m[5], paint.Gradient.Fx, paint.Gradient.Fy));

                uint kind = paint.Type == NanoSvgPaintType.LinearGradient
                    ? SvgRasterizeData.PaintLinearGradient
                    : SvgRasterizeData.PaintRadialGradient;
                paintInfo = kind | ((uint)paint.Gradient.Spread << 2) | ((uint)rampStart << 4);
                break;
            }
            default:
                return -1;
        }

        int shapeIndex = data.ShapeMeta.Count;
        data.ShapeBboxes.Add(default);
        data.ShapeColors.Add(solidColor);
        data.ShapeMeta.Add(default); // filled by EndShape
        return shapeIndex;
    }

    /// <summary>Commits the shape when edges were produced; returns false when the
    /// shape contributed no geometry.</summary>
    private static bool EndShape(SvgRasterizeData data, int shapeIndex, int edgeStart, uint fillRule, uint paintInfo)
    {
        int edgeCount = data.Edges.Count - edgeStart;
        if (edgeCount == 0)
        {
            return false;
        }

        Vector4 bbox = new Vector4(
            float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        for (int i = edgeStart; i < data.Edges.Count; i++)
        {
            Vector4 e = data.Edges[i];
            bbox.X = MathF.Min(bbox.X, MathF.Min(e.X, e.Z));
            bbox.Y = MathF.Min(bbox.Y, MathF.Min(e.Y, e.W));
            bbox.Z = MathF.Max(bbox.Z, MathF.Max(e.X, e.Z));
            bbox.W = MathF.Max(bbox.W, MathF.Max(e.Y, e.W));
        }
        // Dilate by one pixel so antialiased border pixels are still evaluated.
        bbox.X = MathF.Max(bbox.X - 1.0f, 0.0f);
        bbox.Y = MathF.Max(bbox.Y - 1.0f, 0.0f);
        bbox.Z = MathF.Min(bbox.Z + 1.0f, data.Width);
        bbox.W = MathF.Min(bbox.W + 1.0f, data.Height);
        data.ShapeBboxes[shapeIndex] = bbox;

        data.ShapeMeta[shapeIndex] = new uint4(
            (uint)edgeStart, (uint)edgeCount, fillRule, paintInfo);
        return true;
    }

    private static void RemoveEmptyShape(SvgRasterizeData data, int shapeIndex)
    {
        data.ShapeBboxes.RemoveAt(shapeIndex);
        data.ShapeColors.RemoveAt(shapeIndex);
        data.ShapeMeta.RemoveAt(shapeIndex);
    }

    private static Vector4 UnpackColor(uint packed)
    {
        return new Vector4(
            (packed & 0xff) / 255.0f,
            ((packed >> 8) & 0xff) / 255.0f,
            ((packed >> 16) & 0xff) / 255.0f,
            ((packed >> 24) & 0xff) / 255.0f);
    }

    /// <summary>Row-major 2x3 transform composition: t becomes t(s(v)).</summary>
    private static void MultiplyXform(float[] t, float[] s)
    {
        float t0 = t[0] * s[0] + t[1] * s[2];
        float t2 = t[2] * s[0] + t[3] * s[2];
        float t4 = t[4] * s[0] + t[5] * s[2] + s[4];
        t[1] = t[0] * s[1] + t[1] * s[3];
        t[3] = t[2] * s[1] + t[3] * s[3];
        t[5] = t[4] * s[1] + t[5] * s[3] + s[5];
        t[0] = t0;
        t[2] = t2;
        t[4] = t4;
    }

    private static int BakeGradientRamp(SvgRasterizeData data, NanoSvgGradient gradient, float opacity)
    {
        int rampStart = data.GradientRamps.Count;
        List<NanoSvgGradientStop> stops = gradient.Stops;
        int last = stops.Count - 1;
        for (int j = 0; j < SvgRasterizeData.GradientRampSize; j++)
        {
            float t = j / (float)(SvgRasterizeData.GradientRampSize - 1);

            Vector4 color = UnpackColor(stops[0].Color);
            for (int i = 0; i < last; i++)
            {
                float o0 = stops[i].Offset;
                float o1 = stops[i + 1].Offset;
                if (t < o0)
                {
                    color = UnpackColor(stops[i].Color);
                    break;
                }
                if (t >= o0 && t <= o1)
                {
                    float f = o1 > o0 ? (t - o0) / (o1 - o0) : 0.0f;
                    color = Vector4.Lerp(UnpackColor(stops[i].Color), UnpackColor(stops[i + 1].Color), f);
                    break;
                }
                color = UnpackColor(stops[i + 1].Color);
            }

            data.GradientRamps.Add(new Vector4(color.X, color.Y, color.Z, color.W * opacity));
        }
        return rampStart;
    }

    // ------------------------------------------------------------------
    // Flattening.
    // ------------------------------------------------------------------

    private sealed class Contour
    {
        public List<Vector2> Points = [];
        public bool Closed;
    }

    private static Contour FlattenContour(NanoSvgPath path, float scale, float offsetX, float offsetY, float tolerance)
    {
        Contour contour = new Contour
        {
            Closed = path.Closed,
        };

        ReadOnlySpan<float> pts = path.Pts;
        int npts = pts.Length / 2;
        Vector2 prev = TransformPoint(pts[0], pts[1], scale, offsetX, offsetY);
        contour.Points.Add(prev);
        for (int i = 0; i < npts - 1; i += 3)
        {
            Vector2 p1 = TransformPoint(pts[(i + 1) * 2], pts[(i + 1) * 2 + 1], scale, offsetX, offsetY);
            Vector2 p2 = TransformPoint(pts[(i + 2) * 2], pts[(i + 2) * 2 + 1], scale, offsetX, offsetY);
            Vector2 p3 = TransformPoint(pts[(i + 3) * 2], pts[(i + 3) * 2 + 1], scale, offsetX, offsetY);
            FlattenCubic(contour.Points, prev, p1, p2, p3, tolerance, 0);
            prev = p3;
        }

        return contour;
    }

    private static Vector2 TransformPoint(float x, float y, float scale, float offsetX, float offsetY)
    {
        return new Vector2(x * scale + offsetX, y * scale + offsetY);
    }

    private static void FlattenCubic(List<Vector2> output, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tolerance, int depth)
    {
        if (depth >= MaxRecursionDepth || IsFlatEnough(p0, p1, p2, p3, tolerance))
        {
            AddPointDeduped(output, p3);
            return;
        }

        // de Casteljau split at t = 0.5.
        Vector2 p01 = Vector2.Lerp(p0, p1, 0.5f);
        Vector2 p12 = Vector2.Lerp(p1, p2, 0.5f);
        Vector2 p23 = Vector2.Lerp(p2, p3, 0.5f);
        Vector2 p012 = Vector2.Lerp(p01, p12, 0.5f);
        Vector2 p123 = Vector2.Lerp(p12, p23, 0.5f);
        Vector2 p0123 = Vector2.Lerp(p012, p123, 0.5f);

        FlattenCubic(output, p0, p01, p012, p0123, tolerance, depth + 1);
        FlattenCubic(output, p0123, p123, p23, p3, tolerance, depth + 1);
    }

    /// <summary>Appends a point unless it coincides with the previous one; duplicate
    /// consecutive points would degenerate into NaN segment directions downstream.</summary>
    private static void AddPointDeduped(List<Vector2> output, Vector2 point)
    {
        if (output.Count > 0 && (point - output[^1]).LengthSquared() < 1e-12f)
        {
            return;
        }
        output.Add(point);
    }

    private static bool IsFlatEnough(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tolerance)
    {
        Vector2 chord = p3 - p0;
        float chordLengthSq = chord.LengthSquared();
        float distanceTolerance = tolerance * tolerance;

        if (chordLengthSq < 1e-12f)
        {
            // Degenerate chord: compare control points to the end point directly.
            return (p1 - p0).LengthSquared() <= distanceTolerance &&
                   (p2 - p0).LengthSquared() <= distanceTolerance;
        }

        // Perpendicular distance of the control points from the chord, squared.
        float d1 = Cross(chord, p1 - p0);
        float d2 = Cross(chord, p2 - p0);
        return (d1 * d1 + d2 * d2) / chordLengthSq <= distanceTolerance;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    private static void AddContourEdges(SvgRasterizeData data, Contour contour)
    {
        List<Vector2> points = contour.Points;
        for (int i = 0; i < points.Count - 1; i++)
        {
            AddEdge(data, points[i], points[i + 1]);
        }
    }

    private static void AddEdge(SvgRasterizeData data, Vector2 a, Vector2 b)
    {
        if ((b - a).LengthSquared() < 1e-12f)
        {
            return;
        }
        data.Edges.Add(new Vector4(a.X, a.Y, b.X, b.Y));
    }

    // ------------------------------------------------------------------
    // Dashing.
    // ------------------------------------------------------------------

    private static float[] ScaleDashArray(NanoSvgShape shape, float scale)
    {
        if (shape.StrokeDashArray.Length == 0)
        {
            return [];
        }

        // SVG: an odd-length pattern is repeated to make pairs of on/off lengths.
        int count = shape.StrokeDashArray.Length;
        int doubled = count % 2 == 0 ? count : count * 2;
        float[] pattern = new float[doubled];
        for (int i = 0; i < doubled; i++)
        {
            pattern[i] = shape.StrokeDashArray[i % count] * scale;
        }
        return pattern;
    }

    private static IEnumerable<Contour> ApplyDashes(Contour contour, float[] pattern, float dashOffset)
    {
        List<Vector2> points = contour.Points;
        if (points.Count < 2)
        {
            yield break;
        }

        float total = 0.0f;
        foreach (float p in pattern)
        {
            total += p;
        }
        if (total <= 1e-6f)
        {
            yield return contour;
            yield break;
        }

        // Resolve the dash phase: advance whole intervals until the remaining phase
        // lies inside the current interval.
        float phase = dashOffset % total;
        if (phase < 0)
        {
            phase += total;
        }
        int n = pattern.Length;
        int interval = 0;
        bool on = true;
        while (phase >= pattern[interval] - 1e-9f && phase > 1e-9f)
        {
            phase -= pattern[interval];
            interval = (interval + 1) % n;
            on = !on;
        }
        float intervalRemaining = pattern[interval] - phase;

        // A closed contour dashes through the closing segment too.
        int segments = points.Count - 1 + (contour.Closed ? 1 : 0);
        Contour current = new Contour();
        for (int i = 0; i < segments; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            Vector2 delta = b - a;
            float length = delta.Length();
            if (length < 1e-9f)
            {
                continue;
            }
            Vector2 dir = delta / length;
            float position = 0.0f;

            while (position < length - 1e-9f)
            {
                if (on && current.Points.Count == 0)
                {
                    current.Points.Add(a + dir * position);
                }
                float step = MathF.Min(length - position, intervalRemaining);
                position += step;
                intervalRemaining -= step;
                if (on)
                {
                    current.Points.Add(a + dir * position);
                }
                if (intervalRemaining <= 1e-9f)
                {
                    if (on)
                    {
                        if (current.Points.Count >= 2)
                        {
                            yield return current;
                        }
                        current = new Contour();
                    }
                    on = !on;
                    interval = (interval + 1) % n;
                    intervalRemaining = pattern[interval];
                }
            }
        }

        if (on && current.Points.Count >= 2)
        {
            yield return current;
        }
    }

    // ------------------------------------------------------------------
    // Stroke expansion.
    // ------------------------------------------------------------------

    private static void ExpandStroke(List<Vector2> points, bool closed, float halfWidth, NanoSvgLineCap cap,
        NanoSvgLineJoin join, float miterLimit, List<Vector2> scratch, SvgRasterizeData data)
    {
        // Ring size for wrap-around indexing: a closed contour whose last point
        // duplicates the first uses the deduplicated count.
        int count = points.Count;
        if (closed && count > 1 && (points[^1] - points[0]).LengthSquared() < 1e-12f)
        {
            count--;
        }

        if (count < 2)
        {
            // Degenerate polyline: with round caps it renders as a dot.
            if (cap == NanoSvgLineCap.Round && halfWidth > 0 && points.Count > 0)
            {
                AddArcContour(scratch, data, points[0], 0.0f, MathF.PI * 2.0f, halfWidth);
            }
            return;
        }

        // Per-segment quads, each oriented consistently (negative signed area).
        int segmentCount = closed ? count : count - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % count];
            if ((b - a).LengthSquared() < 1e-12f)
            {
                continue;
            }
            Vector2 d = Vector2.Normalize(b - a);
            Vector2 n = new Vector2(-d.Y, d.X);

            scratch.Clear();
            scratch.Add(a + n * halfWidth);
            scratch.Add(b + n * halfWidth);
            scratch.Add(b - n * halfWidth);
            scratch.Add(a - n * halfWidth);
            AddStrokeContour(scratch, data);
        }

        // Joins at interior vertices (for closed contours every vertex is interior).
        int joinStart = closed ? 0 : 1;
        int joinEnd = closed ? count : count - 1;
        for (int i = joinStart; i < joinEnd; i++)
        {
            Vector2 prev = points[(i - 1 + count) % count];
            Vector2 p = points[i];
            Vector2 next = points[(i + 1) % count];
            Vector2 d1 = p - prev;
            Vector2 d2 = next - p;
            if (d1.LengthSquared() < 1e-12f || d2.LengthSquared() < 1e-12f)
            {
                continue;
            }
            d1 = Vector2.Normalize(d1);
            d2 = Vector2.Normalize(d2);
            float turn = Cross(d1, d2);
            if (MathF.Abs(turn) < 1e-6f)
            {
                continue; // collinear
            }

            Vector2 n1 = new Vector2(-d1.Y, d1.X);
            Vector2 n2 = new Vector2(-d2.Y, d2.X);
            // The join wedge fills the outer side of the turn.
            Vector2 outer1 = turn > 0 ? -n1 : n1;
            Vector2 outer2 = turn > 0 ? -n2 : n2;

            switch (join)
            {
                case NanoSvgLineJoin.Round:
                {
                    // Fan covering the wedge, taking the short way between the normals.
                    float a0 = MathF.Atan2(outer1.Y, outer1.X);
                    float a2 = MathF.Atan2(outer2.Y, outer2.X);
                    float sweep = WrapAngle(a2 - a0);
                    AddArcContour(scratch, data, p, a0, a0 + sweep, halfWidth);
                    break;
                }
                case NanoSvgLineJoin.Miter:
                {
                    float theta = MathF.Acos(Math.Clamp(Vector2.Dot(d1, d2), -1.0f, 1.0f)); // turn angle
                    float sinHalf = MathF.Sin(theta * 0.5f);
                    if (sinHalf > 1e-6f && 1.0f / sinHalf <= miterLimit)
                    {
                        Vector2 bisector = Vector2.Normalize(outer1 + outer2);
                        Vector2 miterPoint = p + bisector * (halfWidth / sinHalf);
                        scratch.Clear();
                        scratch.Add(p);
                        scratch.Add(p + outer1 * halfWidth);
                        scratch.Add(miterPoint);
                        scratch.Add(p + outer2 * halfWidth);
                        AddStrokeContour(scratch, data);
                        break;
                    }
                    goto case NanoSvgLineJoin.Bevel;
                }
                case NanoSvgLineJoin.Bevel:
                default:
                {
                    scratch.Clear();
                    scratch.Add(p);
                    scratch.Add(p + outer1 * halfWidth);
                    scratch.Add(p + outer2 * halfWidth);
                    AddStrokeContour(scratch, data);
                    break;
                }
            }
        }

        if (closed)
        {
            return;
        }

        // End caps.
        Vector2 startDir = points[1] - points[0];
        if (startDir.LengthSquared() >= 1e-12f)
        {
            AddCap(scratch, data, points[0], -Vector2.Normalize(startDir), halfWidth, cap);
        }
        Vector2 endDir = points[count - 1] - points[count - 2];
        if (endDir.LengthSquared() >= 1e-12f)
        {
            AddCap(scratch, data, points[count - 1], Vector2.Normalize(endDir), halfWidth, cap);
        }
    }

    /// <summary>Wraps an angle difference into (-pi, pi].</summary>
    private static float WrapAngle(float delta)
    {
        while (delta > MathF.PI)
        {
            delta -= 2.0f * MathF.PI;
        }
        while (delta < -MathF.PI)
        {
            delta += 2.0f * MathF.PI;
        }
        return delta;
    }

    private static void AddCap(List<Vector2> scratch, SvgRasterizeData data, Vector2 p, Vector2 outward,
        float halfWidth, NanoSvgLineCap cap)
    {
        switch (cap)
        {
            case NanoSvgLineCap.Round:
                AddArcContour(scratch, data, p, MathF.Atan2(outward.Y, outward.X) + MathF.PI * 0.5f,
                    MathF.Atan2(outward.Y, outward.X) - MathF.PI * 0.5f, halfWidth);
                break;
            case NanoSvgLineCap.Square:
            {
                Vector2 n = new Vector2(-outward.Y, outward.X);
                scratch.Clear();
                scratch.Add(p + n * halfWidth);
                scratch.Add(p + outward * halfWidth + n * halfWidth);
                scratch.Add(p + outward * halfWidth - n * halfWidth);
                scratch.Add(p - n * halfWidth);
                AddStrokeContour(scratch, data);
                break;
            }
        }
    }

    /// <summary>Emits a closed fan contour: the center point plus an arc from angle a0
    /// sweeping to a1. A full turn renders a dot.</summary>
    private static void AddArcContour(List<Vector2> scratch, SvgRasterizeData data, Vector2 center,
        float a0, float a1, float radius)
    {
        float sweep = a1 - a0;
        if (MathF.Abs(sweep) < 1e-5f)
        {
            return;
        }

        // Arc discretization: one step per ~0.25 rad, bounded.
        int steps = Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) / 0.25f), 2, 32);

        scratch.Clear();
        scratch.Add(center);
        for (int i = 0; i <= steps; i++)
        {
            float angle = a0 + sweep * (i / (float)steps);
            scratch.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
        AddStrokeContour(scratch, data);
    }

    /// <summary>Appends a stroke-outline contour as edges. All pieces of one stroke are
    /// emitted with the same winding (negative signed area) so overlapping pieces stay
    /// filled under the non-zero rule.</summary>
    private static void AddStrokeContour(List<Vector2> points, SvgRasterizeData data)
    {
        if (points.Count < 3)
        {
            return;
        }

        if (SignedArea(points) > 0)
        {
            points.Reverse();
        }

        for (int i = 0; i < points.Count; i++)
        {
            AddEdge(data, points[i], points[(i + 1) % points.Count]);
        }
    }

    private static float SignedArea(List<Vector2> points)
    {
        float area = 0.0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area * 0.5f;
    }
}
