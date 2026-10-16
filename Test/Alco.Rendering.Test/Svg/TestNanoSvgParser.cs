using NUnit.Framework;
using NanoSvgSharp;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>
/// CPU parser tests over the NanoSVG port: element coverage, path data grammar
/// (implicit repetition, arcs), presentation attribute inheritance, gradients,
/// colors, CSS style blocks and viewBox scaling. All tests run without a GPU.
/// </summary>
[TestFixture]
public class TestNanoSvgParser
{
    private const string Header =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">";

    /// <summary>Parses a document body wrapped in a standard 100x100 root.</summary>
    private static NanoSvgImage Parse(string body)
    {
        Assert.That(NanoSvg.TryParse(Header + body + "</svg>", out NanoSvgImage? image), Is.True,
            "The document must parse");
        Assert.That(image, Is.Not.Null);
        return image!;
    }

    /// <summary>Contour anchor points: like upstream NanoSVG, contours store the raw
    /// cubic control polygon (3 points per segment); anchors sit at every third point.</summary>
    private static IEnumerable<float[]> Anchors(NanoSvgPath path)
    {
        for (int i = 0; i * 6 + 1 < path.Pts.Length; i++)
        {
            yield return [path.Pts[i * 6], path.Pts[i * 6 + 1]];
        }
    }

    /// <summary>Malformed XML fails cleanly instead of throwing.</summary>
    [Test]
    public void MalformedXml_ReturnsFalse()
    {
        Assert.That(NanoSvg.TryParse("<svg><rect></svg>", out _), Is.False);
        Assert.That(NanoSvg.TryParse("not xml at all", out _), Is.False);
    }

    /// <summary>A sharp rect parses to one closed contour whose anchors are the
    /// four corners (segments stored as degenerate cubics like upstream).</summary>
    [Test]
    public void Rect_ParsesClosedFourCornerContour()
    {
        NanoSvgImage image = Parse("<rect x=\"10\" y=\"20\" width=\"30\" height=\"15\"/>");
        Assert.That(image.Shapes, Has.Count.EqualTo(1));
        NanoSvgShape shape = image.Shapes[0];
        Assert.That(shape.Paths, Has.Count.EqualTo(1));
        NanoSvgPath path = shape.Paths[0];
        Assert.That(path.Closed, Is.True);
        float[][] anchors = Anchors(path).ToArray();
        Assert.That(anchors, Has.Length.EqualTo(5), "four corners plus the closing anchor");
        Assert.That(anchors[0], Is.EqualTo(new[] { 10f, 20f }).Within(1e-4));
        Assert.That(anchors[1], Is.EqualTo(new[] { 40f, 20f }).Within(1e-4));
        Assert.That(anchors[2], Is.EqualTo(new[] { 40f, 35f }).Within(1e-4));
        Assert.That(anchors[3], Is.EqualTo(new[] { 10f, 35f }).Within(1e-4));
        Assert.That(anchors[4], Is.EqualTo(new[] { 10f, 20f }).Within(1e-4), "closed back to the start");
    }

    /// <summary>Implicit command repetition consumes extra coordinate pairs.</summary>
    [Test]
    public void Path_ImplicitCommandRepetition()
    {
        NanoSvgImage image = Parse("<path d=\"M 10 10 L 20 20 30 10\"/>");
        NanoSvgPath path = image.Shapes[0].Paths[0];
        float[][] anchors = Anchors(path).ToArray();
        Assert.That(anchors, Has.Length.EqualTo(3), "three anchors from one repeated L command");
        Assert.That(anchors[1], Is.EqualTo(new[] { 20f, 20f }).Within(1e-4));
        Assert.That(anchors[2], Is.EqualTo(new[] { 30f, 10f }).Within(1e-4));
    }

    /// <summary>Repeated M commands split the element into several contours.</summary>
    [Test]
    public void Path_MultipleSubpaths()
    {
        NanoSvgImage image = Parse("<path d=\"M 5 5 L 15 5 M 25 25 L 35 35\"/>");
        Assert.That(image.Shapes[0].Paths, Has.Count.EqualTo(2));
    }

    /// <summary>Arc flags parse and the arc lands on its exact endpoint.</summary>
    [Test]
    public void Arc_EndsOnExplicitEndpoint()
    {
        NanoSvgImage image = Parse("<path d=\"M 0 50 A 50 50 0 0 1 100 0\"/>");
        NanoSvgPath path = image.Shapes[0].Paths[0];
        Assert.That(path.Closed, Is.False);
        float[][] anchors = Anchors(path).ToArray();
        Assert.That(anchors, Has.Length.GreaterThan(3), "the arc must subdivide into cubics");
        Assert.That(anchors[0], Is.EqualTo(new[] { 0f, 50f }).Within(1e-4));
        Assert.That(anchors[^1], Is.EqualTo(new[] { 100f, 0f }).Within(1e-3),
            "arc must finish at the endpoint");
    }

    /// <summary>Presentation attributes inherit from groups into children.</summary>
    [Test]
    public void GroupAttributes_InheritIntoChildren()
    {
        NanoSvgImage image = Parse(
            "<g fill=\"red\" stroke=\"blue\" stroke-width=\"2\"><rect x=\"0\" y=\"0\" width=\"10\" height=\"10\"/></g>");
        NanoSvgShape shape = image.Shapes[0];
        Assert.That(shape.Fill.Type, Is.EqualTo(NanoSvgPaintType.Color));
        Assert.That(shape.Fill.Color, Is.EqualTo(0xFF0000FFu), "red = r | a<<24");
        Assert.That(shape.Stroke.Type, Is.EqualTo(NanoSvgPaintType.Color));
        Assert.That(shape.Stroke.Color, Is.EqualTo(0xFFFF0000u), "blue = b<<16 | a<<24");
        Assert.That(shape.StrokeWidth, Is.EqualTo(2).Within(1e-5));
    }

    /// <summary>Named colors, rgb() notation and #hex all produce packed RGBA.</summary>
    [Test]
    public void ColorNotations_PackIdentically()
    {
        NanoSvgImage named = Parse("<rect x=\"0\" y=\"0\" width=\"1\" height=\"1\" fill=\"red\"/>");
        NanoSvgImage rgb = Parse("<rect x=\"0\" y=\"0\" width=\"1\" height=\"1\" fill=\"rgb(255,0,0)\"/>");
        NanoSvgImage hex = Parse("<rect x=\"0\" y=\"0\" width=\"1\" height=\"1\" fill=\"#ff0000\"/>");
        Assert.That(named.Shapes[0].Fill.Color, Is.EqualTo(rgb.Shapes[0].Fill.Color));
        Assert.That(named.Shapes[0].Fill.Color, Is.EqualTo(hex.Shapes[0].Fill.Color));
        Assert.That(named.Shapes[0].Fill.Color, Is.EqualTo(0xFF0000FFu));
    }

    /// <summary>opacity scales the shape factor, fill-opacity bakes into the paint alpha.</summary>
    [Test]
    public void OpacityAttributes_SplitBetweenShapeAndPaint()
    {
        NanoSvgImage image = Parse(
            "<rect x=\"0\" y=\"0\" width=\"1\" height=\"1\" opacity=\"0.5\" fill-opacity=\"0.25\"/>");
        NanoSvgShape shape = image.Shapes[0];
        Assert.That(shape.Opacity, Is.EqualTo(0.5f).Within(1e-5));
        uint alpha = shape.Fill.Color >> 24;
        Assert.That(alpha, Is.EqualTo(63u), "0.25 * 255 truncates to 63");
    }

    /// <summary>fill-rule parses onto the shape.</summary>
    [Test]
    public void FillRule_Parses()
    {
        NanoSvgImage image = Parse(
            "<path d=\"M0 0h10v10h-10z M2 2h6v6h-6z\" fill-rule=\"evenodd\"/>");
        Assert.That(image.Shapes[0].FillRule, Is.EqualTo(NanoSvgFillRule.EvenOdd));
    }

    /// <summary>Gradient stops resolve through url() and end up sorted by offset.</summary>
    [Test]
    public void LinearGradient_ResolvesUrlAndSortsStops()
    {
        NanoSvgImage image = Parse(
            """
            <defs>
                <linearGradient id="g1" x1="0%" y1="0%" x2="0%" y2="100%">
                    <stop offset="1" stop-color="blue"/>
                    <stop offset="0" stop-color="red"/>
                </linearGradient>
            </defs>
            <rect x="0" y="0" width="10" height="10" fill="url(#g1)"/>
            """);
        NanoSvgShape shape = image.Shapes[0];
        Assert.That(shape.Fill.Type, Is.EqualTo(NanoSvgPaintType.LinearGradient));
        Assert.That(shape.Fill.Gradient, Is.Not.Null);
        Assert.That(shape.Fill.Gradient!.Stops, Has.Count.EqualTo(2));
        Assert.That(shape.Fill.Gradient.Stops[0].Offset, Is.EqualTo(0).Within(1e-5));
        Assert.That(shape.Fill.Gradient.Stops[0].Color, Is.EqualTo(0xFF0000FFu));
        Assert.That(shape.Fill.Gradient.Stops[1].Offset, Is.EqualTo(1).Within(1e-5));
        Assert.That(shape.Fill.Gradient.Stops[1].Color, Is.EqualTo(0xFFFF0000u));
    }

    /// <summary>A missing url() reference degrades to no paint instead of failing the parse.</summary>
    [Test]
    public void MissingGradientReference_DegradesToNone()
    {
        NanoSvgImage image = Parse(
            "<rect x=\"0\" y=\"0\" width=\"1\" height=\"1\" fill=\"url(#missing)\"/>");
        Assert.That(image.Shapes[0].Fill.Type, Is.EqualTo(NanoSvgPaintType.None));
    }

    /// <summary>An invalid (zero-radius) radial gradient degrades to no paint.</summary>
    [Test]
    public void DegenerateRadialGradient_DegradesToNone()
    {
        NanoSvgImage image = Parse(
            """
            <defs><radialGradient id="r1" r="0"><stop offset="0" stop-color="red"/></radialGradient></defs>
            <rect x="0" y="0" width="1" height="1" fill="url(#r1)"/>
            """);
        Assert.That(image.Shapes[0].Fill.Type, Is.EqualTo(NanoSvgPaintType.None));
    }

    /// <summary>viewBox coordinates rescale into the width/height pixel space.</summary>
    [Test]
    public void ViewBox_ScalesGeometryToPixelSize()
    {
        Assert.That(NanoSvg.TryParse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\" " +
            "viewBox=\"0 0 200 100\"><rect x=\"100\" y=\"50\" width=\"100\" height=\"50\"/></svg>",
            out NanoSvgImage? image), Is.True);
        Assert.That(image!.Width, Is.EqualTo(100).Within(1e-4));
        Assert.That(image.Height, Is.EqualTo(50).Within(1e-4));
        float[][] anchors = Anchors(image.Shapes[0].Paths[0]).ToArray();
        Assert.That(anchors[0], Is.EqualTo(new[] { 50f, 25f }).Within(1e-4));
        Assert.That(anchors[1], Is.EqualTo(new[] { 100f, 25f }).Within(1e-4));
        Assert.That(anchors[2], Is.EqualTo(new[] { 100f, 50f }).Within(1e-4));
        Assert.That(anchors[3], Is.EqualTo(new[] { 50f, 50f }).Within(1e-4));
    }

    /// <summary>Absent width/height falls back to the viewBox size.</summary>
    [Test]
    public void ViewBoxOnly_ProvidesIntrinsicSize()
    {
        Assert.That(NanoSvg.TryParse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 32\"/>",
            out NanoSvgImage? image), Is.True);
        Assert.That(image!.Width, Is.EqualTo(64).Within(1e-4));
        Assert.That(image.Height, Is.EqualTo(32).Within(1e-4));
    }

    /// <summary>Unit suffixes on the root size convert to pixels at the parser DPI.</summary>
    [Test]
    public void UnitSuffix_ConvertsRootSizeAndGeometryToPixels()
    {
        Assert.That(NanoSvg.TryParse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10mm\" height=\"10mm\" " +
            "viewBox=\"0 0 10 10\"><rect x=\"0\" y=\"0\" width=\"5\" height=\"5\"/></svg>",
            out NanoSvgImage? image), Is.True);
        float pxPerMm = 96.0f / 25.4f;
        Assert.That(image!.Width, Is.EqualTo(10 * pxPerMm).Within(1e-3));
        float[][] anchors = Anchors(image.Shapes[0].Paths[0]).ToArray();
        Assert.That(anchors[1][0], Is.EqualTo(5 * pxPerMm).Within(1e-3), "corner anchor in pixels");
    }

    /// <summary>CSS style blocks apply to elements through their class attribute.</summary>
    [Test]
    public void CssStyleBlock_AppliesViaClassAttribute()
    {
        NanoSvgImage image = Parse(
            """
            <style>.box { fill: #00ff00; stroke-width: 3px; }</style>
            <rect class="box" x="0" y="0" width="1" height="1"/>
            """);
        NanoSvgShape shape = image.Shapes[0];
        Assert.That(shape.Fill.Color, Is.EqualTo(0xFF00FF00u), "green = g<<8 | a<<24");
        Assert.That(shape.StrokeWidth, Is.EqualTo(3).Within(1e-5));
    }

    /// <summary>Transforms on groups compose onto shape geometry.</summary>
    [Test]
    public void GroupTransform_ComposesOntoGeometry()
    {
        NanoSvgImage image = Parse(
            "<g transform=\"translate(10,5) scale(2)\"><rect x=\"1\" y=\"1\" width=\"2\" height=\"2\"/></g>");
        float[][] anchors = Anchors(image.Shapes[0].Paths[0]).ToArray();
        Assert.That(anchors[0], Is.EqualTo(new[] { 12f, 7f }).Within(1e-4));
        Assert.That(anchors[1], Is.EqualTo(new[] { 16f, 7f }).Within(1e-4));
        Assert.That(anchors[2], Is.EqualTo(new[] { 16f, 11f }).Within(1e-4));
        Assert.That(anchors[3], Is.EqualTo(new[] { 12f, 11f }).Within(1e-4));
    }
}
