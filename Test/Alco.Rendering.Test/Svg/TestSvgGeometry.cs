using System.Numerics;
using NUnit.Framework;
using NanoSvgSharp;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>
/// CPU geometry tests over SvgGeometryUtility.Build: device-space edge emission,
/// bounding-box dilation, paint metadata encoding, flattening endpoints, stroke
/// expansion (dashing, winding consistency) and gradient ramp baking. All tests
/// run without a GPU.
/// </summary>
[TestFixture]
public class TestSvgGeometry
{
    private const string Header =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">";

    /// <summary>Parses a document body and builds rasterization data at a target size.</summary>
    private static SvgRasterizeData Build(string body, uint width = 100, uint height = 100)
    {
        Assert.That(NanoSvg.TryParse(Header + body + "</svg>", out NanoSvgImage? image), Is.True,
            "The document must parse");
        return SvgGeometryUtility.Build(image!, width, height);
    }

    /// <summary>A rect fill emits exactly four edges with a 1px-dilated bbox.</summary>
    [Test]
    public void RectFill_FourEdgesWithDilatedBbox()
    {
        SvgRasterizeData data = Build("<rect x=\"10\" y=\"20\" width=\"30\" height=\"15\"/>");
        Assert.That(data.Edges, Has.Count.EqualTo(4));
        Assert.That(data.ShapeMeta, Has.Count.EqualTo(1));
        Assert.That(data.ShapeMeta[0], Is.EqualTo(new uint4(0, 4, 0, SvgRasterizeData.PaintSolid)));
        Assert.That(data.ShapeBboxes[0],
            Is.EqualTo(new Vector4(9, 19, 41, 36)).Within(1e-4), "geometry bounds dilated by one pixel");
    }

    /// <summary>The default fill color is opaque black in straight alpha.</summary>
    [Test]
    public void DefaultFill_BlackStraightAlpha()
    {
        SvgRasterizeData data = Build("<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\"/>");
        Assert.That(data.ShapeColors[0], Is.EqualTo(new Vector4(0, 0, 0, 1)).Within(1e-5));
    }

    /// <summary>Shape opacity multiplies only the paint alpha channel.</summary>
    [Test]
    public void ShapeOpacity_MultipliesPaintAlpha()
    {
        SvgRasterizeData data = Build(
            "<rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" fill=\"#ff0000\" opacity=\"0.5\"/>");
        Assert.That(data.ShapeColors[0], Is.EqualTo(new Vector4(1, 0, 0, 0.5f)).Within(1e-4));
    }

    /// <summary>fill-rule="evenodd" reaches the shape metadata.</summary>
    [Test]
    public void EvenOddFill_EncodedInShapeMeta()
    {
        SvgRasterizeData data = Build(
            "<path d=\"M0 0h10v10h-10z M2 2h6v6h-6z\" fill-rule=\"evenodd\"/>");
        Assert.That(data.ShapeMeta[0].Z, Is.EqualTo(1u));
    }

    /// <summary>Flattened curves keep their exact endpoints and subdivide in between.</summary>
    [Test]
    public void FlattenedCubic_EndpointsExactAndSubdivided()
    {
        SvgRasterizeData data = Build("<path d=\"M 10 10 C 20 0 30 50 40 10\"/>");
        Assert.That(data.Edges, Has.Count.GreaterThanOrEqualTo(4), "the curve must subdivide");
        Vector4 first = data.Edges[0];
        Vector4 last = data.Edges[^1];
        Assert.That(new Vector2(first.X, first.Y), Is.EqualTo(new Vector2(10, 10)).Within(1e-4));
        Assert.That(new Vector2(last.Z, last.W), Is.EqualTo(new Vector2(40, 10)).Within(1e-4));
    }

    /// <summary>Stroke geometry expands half the stroke width around the spine,
    /// capped flat with butt caps.</summary>
    [Test]
    public void ButtCapStroke_BboxHalfWidthAroundSpine()
    {
        SvgRasterizeData data = Build(
            "<line x1=\"20\" y1=\"50\" x2=\"80\" y2=\"50\" fill=\"none\" " +
            "stroke=\"black\" stroke-width=\"10\"/>");
        Assert.That(data.ShapeMeta, Has.Count.EqualTo(1));
        Assert.That(data.ShapeBboxes[0],
            Is.EqualTo(new Vector4(19, 44, 81, 56)).Within(1e-4),
            "spine y 50 +/- 5, dilated by one pixel; butt caps do not extend x");
    }

    /// <summary>Dashing splits the spine into one quad per on-interval: a
    /// 100px spine with pattern "10 10" yields five 4-edge quads.</summary>
    [Test]
    public void DashArray_SplitsSpineIntoQuads()
    {
        SvgRasterizeData dashed = Build(
            "<line x1=\"0\" y1=\"50\" x2=\"100\" y2=\"50\" fill=\"none\" " +
            "stroke=\"black\" stroke-width=\"2\" stroke-dasharray=\"10 10\"/>");
        SvgRasterizeData solid = Build(
            "<line x1=\"0\" y1=\"50\" x2=\"100\" y2=\"50\" fill=\"none\" stroke=\"black\" stroke-width=\"2\"/>");
        Assert.That(solid.Edges, Has.Count.EqualTo(4), "one quad for the whole spine");
        Assert.That(dashed.Edges, Has.Count.EqualTo(20), "five dashes, four edges each");
    }

    /// <summary>All stroke pieces wind consistently (negative signed area) so the
    /// nonzero rule fills overlapping joins and dashes without holes.</summary>
    [Test]
    public void DashQuads_WindConsistentlyNegative()
    {
        SvgRasterizeData data = Build(
            "<line x1=\"0\" y1=\"50\" x2=\"100\" y2=\"50\" fill=\"none\" " +
            "stroke=\"black\" stroke-width=\"2\" stroke-dasharray=\"10 10\"/>");
        for (int dash = 0; dash * 4 + 3 < data.Edges.Count; dash++)
        {
            Vector2[] quad =
            [
                new(data.Edges[dash * 4].X, data.Edges[dash * 4].Y),
                new(data.Edges[dash * 4].Z, data.Edges[dash * 4].W),
                new(data.Edges[dash * 4 + 1].Z, data.Edges[dash * 4 + 1].W),
                new(data.Edges[dash * 4 + 2].Z, data.Edges[dash * 4 + 2].W),
            ];
            float area = 0;
            for (int i = 0; i < quad.Length; i++)
            {
                Vector2 a = quad[i];
                Vector2 b = quad[(i + 1) % quad.Length];
                area += a.X * b.Y - b.X * a.Y;
            }
            Assert.That(area, Is.Negative, $"dash {dash} must wind negatively");
        }
    }

    /// <summary>Round caps emit an arc fan that widens the bbox beyond the spine.</summary>
    [Test]
    public void RoundCapStroke_BboxExtendsBeyondSpine()
    {
        SvgRasterizeData data = Build(
            "<line x1=\"20\" y1=\"50\" x2=\"80\" y2=\"50\" fill=\"none\" " +
            "stroke=\"black\" stroke-width=\"10\" stroke-linecap=\"round\"/>");
        Vector4 bbox = data.ShapeBboxes[0];
        Assert.That(bbox.X, Is.LessThan(15), "the start cap extends the bbox");
        Assert.That(bbox.Z, Is.GreaterThan(85), "the end cap extends the bbox");
        Assert.That(data.Edges, Has.Count.GreaterThan(4), "caps add geometry beyond the quad");
    }

    /// <summary>Linear gradients bake a 64-entry ramp and encode kind+spread+ramp
    /// offset into paintInfo; the color slot stays unused.</summary>
    [Test]
    public void LinearGradient_BakesRampAndPaintInfo()
    {
        SvgRasterizeData data = Build(
            """
            <defs>
                <linearGradient id="g" x1="0%" y1="0%" x2="0%" y2="100%">
                    <stop offset="0" stop-color="#ff0000"/>
                    <stop offset="1" stop-color="#0000ff"/>
                </linearGradient>
            </defs>
            <rect x="0" y="0" width="100" height="100" fill="url(#g)"/>
            """);
        Assert.That(data.ShapeMeta, Has.Count.EqualTo(1));
        uint paintInfo = data.ShapeMeta[0].W;
        Assert.That(paintInfo & 0x3u, Is.EqualTo(SvgRasterizeData.PaintLinearGradient));
        Assert.That((paintInfo >> 2) & 0x3u, Is.EqualTo(0u), "pad spread");
        Assert.That(paintInfo >> 4, Is.EqualTo(0u), "first ramp");
        Assert.That(data.GradientRamps, Has.Count.EqualTo(SvgRasterizeData.GradientRampSize));
        Assert.That(data.GradientRamps[0], Is.EqualTo(new Vector4(1, 0, 0, 1)).Within(1e-4));
        Assert.That(data.GradientRamps[^1], Is.EqualTo(new Vector4(0, 0, 1, 1)).Within(1e-4));
        Vector4 mid = data.GradientRamps[SvgRasterizeData.GradientRampSize / 2];
        float midT = 0.5f * SvgRasterizeData.GradientRampSize / (SvgRasterizeData.GradientRampSize - 1);
        Assert.That(mid, Is.EqualTo(new Vector4(1 - midT, 0, midT, 1)).Within(0.01));
        // One transform pair per gradient shape.
        Assert.That(data.GradientXforms, Has.Count.EqualTo(2));
    }

    /// <summary>Radial gradients encode their paint kind.</summary>
    [Test]
    public void RadialGradient_EncodesPaintKind()
    {
        SvgRasterizeData data = Build(
            """
            <defs>
                <radialGradient id="r" cx="50%" cy="50%" r="50%">
                    <stop offset="0" stop-color="#ffffff"/>
                    <stop offset="1" stop-color="#000000"/>
                </radialGradient>
            </defs>
            <rect x="0" y="0" width="100" height="100" fill="url(#r)"/>
            """);
        Assert.That(data.ShapeMeta[0].W & 0x3u, Is.EqualTo(SvgRasterizeData.PaintRadialGradient));
        Assert.That(data.GradientRamps, Has.Count.EqualTo(SvgRasterizeData.GradientRampSize));
    }

    /// <summary>Gradient stops with opacity bake the alpha into the ramp.</summary>
    [Test]
    public void GradientStopOpacity_BakesIntoRampAlpha()
    {
        SvgRasterizeData data = Build(
            """
            <defs>
                <linearGradient id="g" x1="0%" y1="0%" x2="100%" y2="0%">
                    <stop offset="0" stop-color="#ff0000" stop-opacity="0.5"/>
                    <stop offset="1" stop-color="#ff0000" stop-opacity="0.5"/>
                </linearGradient>
            </defs>
            <rect x="0" y="0" width="100" height="100" fill="url(#g)"/>
            """);
        Assert.That(data.GradientRamps[0].W, Is.EqualTo(0.5f).Within(0.01));
    }

    /// <summary>Fit scaling maps the image into a larger target with centering.</summary>
    [Test]
    public void LargerTarget_ScalesAndCentersGeometry()
    {
        Assert.That(NanoSvg.TryParse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\" " +
            "viewBox=\"0 0 10 10\"><rect x=\"0\" y=\"0\" width=\"10\" height=\"10\"/></svg>",
            out NanoSvgImage? image), Is.True);
        SvgRasterizeData data = SvgGeometryUtility.Build(image!, 100, 100);
        Vector4 bbox = data.ShapeBboxes[0];
        // Fill the whole target: dilated bounds clamp to [0, 100].
        Assert.That(bbox.X, Is.EqualTo(0).Within(1e-3));
        Assert.That(bbox.Y, Is.EqualTo(0).Within(1e-3));
        Assert.That(bbox.Z, Is.EqualTo(100).Within(1e-3));
        Assert.That(bbox.W, Is.EqualTo(100).Within(1e-3));
    }

    /// <summary>A non-square target letterboxes the image on the wider axis.</summary>
    [Test]
    public void WideTarget_CentersWithLetterbox()
    {
        Assert.That(NanoSvg.TryParse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\" " +
            "viewBox=\"0 0 10 10\"><rect x=\"0\" y=\"0\" width=\"10\" height=\"10\"/></svg>",
            out NanoSvgImage? image), Is.True);
        // Fit 10x10 into 200x100: scale 10, centered horizontally at x 50..150.
        SvgRasterizeData data = SvgGeometryUtility.Build(image!, 200, 100);
        Vector4 bbox = data.ShapeBboxes[0];
        Assert.That(bbox.X, Is.EqualTo(49).Within(1e-3));
        Assert.That(bbox.Z, Is.EqualTo(151).Within(1e-3));
        Assert.That(bbox.Y, Is.EqualTo(0).Within(1e-3), "dilation clamps at zero");
        Assert.That(bbox.W, Is.EqualTo(100).Within(1e-3), "dilation clamps at the height");
    }

    /// <summary>Shapes without visible geometry drop their reserved paint slot.</summary>
    [Test]
    public void InvisibleShapes_ProduceNoShapeSlots()
    {
        SvgRasterizeData data = Build(
            "<rect x=\"0\" y=\"0\" width=\"0\" height=\"0\"/>" +
            "<rect x=\"5\" y=\"5\" width=\"5\" height=\"5\" display=\"none\"/>" +
            "<rect x=\"10\" y=\"10\" width=\"5\" height=\"5\" fill=\"none\" stroke=\"none\"/>");
        Assert.That(data.Edges, Is.Empty);
        Assert.That(data.ShapeMeta, Is.Empty);
    }

    /// <summary>Stroke rendering adds a second shape slot after the fill.</summary>
    [Test]
    public void FillAndStroke_EmitTwoShapeSlotsInOrder()
    {
        SvgRasterizeData data = Build(
            "<rect x=\"10\" y=\"10\" width=\"20\" height=\"20\" " +
            "fill=\"#ff0000\" stroke=\"#0000ff\" stroke-width=\"4\"/>");
        Assert.That(data.ShapeMeta, Has.Count.EqualTo(2));
        Assert.That(data.ShapeColors[0], Is.EqualTo(new Vector4(1, 0, 0, 1)).Within(1e-4));
        Assert.That(data.ShapeColors[1], Is.EqualTo(new Vector4(0, 0, 1, 1)).Within(1e-4));
        // Stroke bbox strictly larger than the fill bbox.
        Assert.That(data.ShapeBboxes[1].X, Is.LessThan(data.ShapeBboxes[0].X));
        Assert.That(data.ShapeBboxes[1].Z, Is.GreaterThan(data.ShapeBboxes[0].Z));
        // Stroke slot starts after the fill edges.
        Assert.That(data.ShapeMeta[1].X, Is.EqualTo(data.ShapeMeta[0].X + data.ShapeMeta[0].Y));
    }

    /// <summary>Zero target dimensions are rejected.</summary>
    [Test]
    public void ZeroTarget_Throws()
    {
        Assert.That(NanoSvg.TryParse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"/>",
            out NanoSvgImage? image), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SvgGeometryUtility.Build(image!, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SvgGeometryUtility.Build(image!, 10, 0));
    }
}
