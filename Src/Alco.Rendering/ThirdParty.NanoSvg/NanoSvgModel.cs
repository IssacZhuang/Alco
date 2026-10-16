// C# port of the NanoSVG parser (https://github.com/memononen/nanosvg), parser only —
// the CPU rasterizer half of the library is intentionally not ported (rasterization
// happens on the GPU in Alco.Rendering).
//
// Copyright (c) 2013-14 Mikko Mononen memon@inside.org
//
// This software is provided 'as-is', without any express or implied
// warranty.  In no event will the authors be held liable for any damages
// arising from the use of this software.
//
// Permission is granted to anyone to use this software for any purpose,
// including commercial applications, and to alter it and redistribute it
// freely, subject to the following restrictions:
//
// 1. The origin of this software must not be misrepresented; you must not
// claim that you wrote the original software. If you use this software
// in a product, an acknowledgment in the product documentation would be
// appreciated but is not required.
// 2. Altered source versions must be plainly marked as such, and must not be
// misrepresented as being the original software.
// 3. This notice may not be removed or altered from any source distribution.
//
// The SVG parser is based on Anti-Grain Geometry 2.4 SVG example
// Copyright (C) 2002-2004 Maxim Shemanarev (McSeem) (http://www.antigrain.com/)
//
// Arc calculation code based on canvg (https://code.google.com/p/canvg/)
//
// Bounding box calculation based on http://blog.hackers-cafe.net/2009/06/how-to-calculate-bezier-curves-bounding.html
//
// Deliberate divergences from the original C code (marked "PORT:" where they occur):
// - The XML layer uses System.Xml.Linq instead of the hand-rolled streaming parser;
//   element and attribute matching uses local names so namespaced documents parse.
//   Malformed XML that the lenient C parser tolerated now fails parsing.
// - preserveAspectRatio defaults to the SVG spec default (xMidYMid meet) instead of
//   the C code's zero-initialized "none".
// - Degenerate matrix inversion resets the inverse to identity (the C code wrote
//   identity into the source matrix and left the inverse uninitialized).
// - Path lists store contours in document order (the C code prepended); contour
//   order does not affect rasterization.

namespace NanoSvgSharp;

/// <summary>Paint type of a fill or stroke. Mirrors NSVGpaintType.</summary>
public enum NanoSvgPaintType : sbyte
{
    /// <summary>Pending url() reference, resolved after parsing.</summary>
    Undefined = -1,
    /// <summary>No paint.</summary>
    None = 0,
    /// <summary>Solid color (0xRRGGBBAA is wrong: see <see cref="NanoSvgPaint.Color"/> packing).</summary>
    Color = 1,
    /// <summary>Linear gradient.</summary>
    LinearGradient = 2,
    /// <summary>Radial gradient.</summary>
    RadialGradient = 3,
}

/// <summary>Gradient spread method. Mirrors NSVGspreadType.</summary>
public enum NanoSvgSpreadType : byte
{
    /// <summary>Clamp the gradient parameter to [0, 1].</summary>
    Pad = 0,
    /// <summary>Mirror-repeat the gradient.</summary>
    Reflect = 1,
    /// <summary>Repeat the gradient.</summary>
    Repeat = 2,
}

/// <summary>Stroke join style. Mirrors NSVGlineJoin.</summary>
public enum NanoSvgLineJoin : byte
{
    /// <summary>Sharp outer corner, clipped by the miter limit.</summary>
    Miter = 0,
    /// <summary>Rounded corner.</summary>
    Round = 1,
    /// <summary>Flattened corner.</summary>
    Bevel = 2,
}

/// <summary>Stroke cap style. Mirrors NSVGlineCap.</summary>
public enum NanoSvgLineCap : byte
{
    /// <summary>End the stroke exactly at the endpoint.</summary>
    Butt = 0,
    /// <summary>Semicircular cap.</summary>
    Round = 1,
    /// <summary>Rectangular cap extending half the stroke width.</summary>
    Square = 2,
}

/// <summary>Fill rule. Mirrors NSVGfillRule.</summary>
public enum NanoSvgFillRule : byte
{
    /// <summary>Non-zero winding number.</summary>
    NonZero = 0,
    /// <summary>Even-odd rule.</summary>
    EvenOdd = 1,
}

/// <summary>Shape flags. Mirrors NSVGflags.</summary>
[Flags]
public enum NanoSvgShapeFlags : byte
{
    /// <summary>The shape is visible.</summary>
    Visible = 0x01,
}

/// <summary>One color stop of a gradient. Mirrors NSVGgradientStop.</summary>
public readonly struct NanoSvgGradientStop
{
    /// <summary>Stop color, packed r | g &lt;&lt; 8 | b &lt;&lt; 16 | a &lt;&lt; 24.</summary>
    public uint Color { get; }

    /// <summary>Stop offset in [0, 1] along the gradient.</summary>
    public float Offset { get; }

    /// <summary>Initializes a stop.</summary>
    public NanoSvgGradientStop(uint color, float offset)
    {
        Color = color;
        Offset = offset;
    }
}

/// <summary>A resolved gradient attached to a shape. Mirrors NSVGgradient.</summary>
public sealed class NanoSvgGradient
{
    /// <summary>Affine transform [a b c d e f] (row-major 2x3, like the C xform[6])
    /// mapping final image pixels into gradient space: linear gradients use the
    /// y coordinate in [0,1], radial gradients use the unit circle centered at the
    /// origin. Already inverted and view-scaled by the parser.</summary>
    public float[] Xform { get; } = new float[6];

    /// <summary>Spread method.</summary>
    public NanoSvgSpreadType Spread { get; set; }

    /// <summary>Focal point in gradient space (unit circle); (0,0) means centered.</summary>
    public float Fx { get; set; }

    /// <summary>Focal point in gradient space (unit circle); (0,0) means centered.</summary>
    public float Fy { get; set; }

    /// <summary>Color stops sorted by offset.</summary>
    public List<NanoSvgGradientStop> Stops { get; } = [];
}

/// <summary>Fill or stroke paint. Mirrors NSVGpaint.</summary>
public struct NanoSvgPaint
{
    /// <summary>Paint type.</summary>
    public NanoSvgPaintType Type;

    /// <summary>Solid color when <see cref="Type"/> is <see cref="NanoSvgPaintType.Color"/>,
    /// packed r | g &lt;&lt; 8 | b &lt;&lt; 16 | a &lt;&lt; 24.</summary>
    public uint Color;

    /// <summary>Gradient when <see cref="Type"/> is a gradient type.</summary>
    public NanoSvgGradient? Gradient;

    /// <summary>Creates a paint of the given type.</summary>
    public NanoSvgPaint(NanoSvgPaintType type)
    {
        Type = type;
        Color = 0;
        Gradient = null;
    }
}

/// <summary>One closed or open contour of a shape, as cubic bezier segments.
/// Mirrors NSVGpath. Points are in final image pixel space after the
/// viewBox transform: [x0,y0, cp1x,cp1y, cp2x,cp2y, x1,y1, ...].</summary>
public sealed class NanoSvgPath
{
    /// <summary>Flattened point pairs; 1 + 3*n points for n cubic segments.</summary>
    public float[] Pts { get; set; } = [];

    /// <summary>Whether the contour is closed.</summary>
    public bool Closed { get; set; }

    /// <summary>Tight bounding box [minx, miny, maxx, maxy].</summary>
    public float[] Bounds { get; } = new float[4];
}

/// <summary>A renderable shape with its fill/stroke paints. Mirrors NSVGshape.</summary>
public sealed class NanoSvgShape
{
    /// <summary>Optional 'id' attribute of the shape or its group.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Fill paint (with fill-opacity folded into the color alpha).</summary>
    public NanoSvgPaint Fill { get; set; }

    /// <summary>'id' of the referenced fill gradient; only used during parsing.</summary>
    public string FillGradientId { get; set; } = string.Empty;

    /// <summary>'id' of the referenced stroke gradient; only used during parsing.</summary>
    public string StrokeGradientId { get; set; } = string.Empty;

    /// <summary>Root transform of the shape at parse time; only used during parsing
    /// (object-bounds gradient resolution).</summary>
    public float[] Xform { get; set; } = [1, 0, 0, 1, 0, 0];

    /// <summary>Stroke paint (with stroke-opacity folded into the color alpha).</summary>
    public NanoSvgPaint Stroke { get; set; }

    /// <summary>Shape opacity (group opacity is approximated by folding into paints).</summary>
    public float Opacity { get; set; } = 1.0f;

    /// <summary>Stroke width in image pixels (view- and transform-scaled).</summary>
    public float StrokeWidth { get; set; } = 1.0f;

    /// <summary>Stroke dash offset in image pixels.</summary>
    public float StrokeDashOffset { get; set; }

    /// <summary>Stroke dash pattern in image pixels.</summary>
    public float[] StrokeDashArray { get; set; } = [];

    /// <summary>Stroke join style.</summary>
    public NanoSvgLineJoin StrokeLineJoin { get; set; } = NanoSvgLineJoin.Miter;

    /// <summary>Stroke cap style.</summary>
    public NanoSvgLineCap StrokeLineCap { get; set; } = NanoSvgLineCap.Butt;

    /// <summary>Miter limit (default 4).</summary>
    public float MiterLimit { get; set; } = 4.0f;

    /// <summary>Fill rule.</summary>
    public NanoSvgFillRule FillRule { get; set; } = NanoSvgFillRule.NonZero;

    /// <summary>Encoded paint order, three 2-bit slots (0 fill, 1 markers, 2 stroke).</summary>
    public byte PaintOrder { get; set; }

    /// <summary>Visibility flags.</summary>
    public NanoSvgShapeFlags Flags { get; set; } = NanoSvgShapeFlags.Visible;

    /// <summary>Tight bounding box of all contours [minx, miny, maxx, maxy].</summary>
    public float[] Bounds { get; } = new float[4];

    /// <summary>Contours of the shape in document order.</summary>
    public List<NanoSvgPath> Paths { get; } = [];
}

/// <summary>A parsed SVG image: intrinsic size plus the shape display list.
/// Mirrors NSVGimage. All geometry is in image pixel space.</summary>
public sealed class NanoSvgImage
{
    /// <summary>Image width in pixels (from the width attribute or the viewBox).</summary>
    public float Width { get; set; }

    /// <summary>Image height in pixels (from the height attribute or the viewBox).</summary>
    public float Height { get; set; }

    /// <summary>Shapes in document order.</summary>
    public List<NanoSvgShape> Shapes { get; } = [];
}
