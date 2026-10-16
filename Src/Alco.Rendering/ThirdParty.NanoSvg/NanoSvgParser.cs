// C# port of the NanoSVG parser (https://github.com/memononen/nanosvg) — see the
// license and port notes in NanoSvgModel.cs.

using System.Globalization;
using System.Xml.Linq;

namespace NanoSvgSharp;

/// <summary>Parses an SVG document into a flat shape list of cubic bezier contours,
/// with styles resolved, gradients created and all geometry transformed into image
/// pixel space (viewBox applied). Text, filters, masks, clip paths, symbols and
/// marker rendering are not supported, matching the original library.</summary>
public static class NanoSvg
{
    private const float Pi = 3.14159265358979323846264338327f;
    private const float Kappa90 = 0.5522847493f;

    private const int AlignMin = 0;
    private const int AlignMid = 1;
    private const int AlignMax = 2;
    private const int AlignNone = 0;
    private const int AlignMeet = 1;
    private const int AlignSlice = 2;

    private const int MaxAttrs = 128;
    private const int MaxDashes = 8;
    private const int MaxClasses = 32;

    private static uint Rgb(uint r, uint g, uint b) => r | (g << 8) | (b << 16);

    /// <summary>Parses an SVG document string. Units and DPI control how absolute
    /// lengths (pt, mm, ...) convert to pixels; "px" and 96 need no conversion.</summary>
    /// <param name="svg">The SVG document text.</param>
    /// <param name="units">Target unit, one of px/pt/pc/mm/cm/in.</param>
    /// <param name="dpi">Dots per inch used for unit conversion.</param>
    /// <param name="image">The parsed image, or null on failure.</param>
    /// <returns>true when the document parsed into an image.</returns>
    public static bool TryParse(string svg, string units, float dpi, out NanoSvgImage? image)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(svg, LoadOptions.None);
        }
        catch (Exception)
        {
            image = null;
            return false;
        }

        if (doc.Root == null)
        {
            image = null;
            return false;
        }

        Parser p = new Parser(dpi);
        try
        {
            Walk(p, doc.Root);
            p.CreateGradients();
            p.ScaleToViewbox(units);
        }
        catch (Exception)
        {
            image = null;
            return false;
        }

        image = p.Image;
        return true;
    }

    /// <summary>Parses an SVG document string in pixels at 96 DPI.</summary>
    /// <param name="svg">The SVG document text.</param>
    /// <param name="image">The parsed image, or null on failure.</param>
    /// <returns>true when the document parsed into an image.</returns>
    public static bool TryParse(string svg, out NanoSvgImage? image)
    {
        return TryParse(svg, "px", 96.0f, out image);
    }

    private static void Walk(Parser p, XElement el)
    {
        p.StartElement(el);
        foreach (XElement child in el.Elements())
        {
            Walk(p, child);
        }
        p.EndElement(el);
    }

    private enum Units
    {
        User,
        Px,
        Pt,
        Pc,
        Mm,
        Cm,
        Inch,
        Percent,
        Em,
        Ex,
    }

    private readonly struct Coordinate
    {
        public readonly float Value;
        public readonly Units Unit;

        public Coordinate(float value, Units unit)
        {
            Value = value;
            Unit = unit;
        }
    }

    private sealed class GradientData
    {
        public string Id = string.Empty;
        public string Ref = string.Empty;
        public NanoSvgPaintType Type;
        public NanoSvgSpreadType Spread;
        public bool ObjectSpaceUnits = true;
        public readonly float[] Xform = new float[6];
        public readonly List<NanoSvgGradientStop> Stops = [];
        public Coordinate Lx1, Ly1, Lx2, Ly2;
        public Coordinate Cx, Cy, R, Fx, Fy;
    }

    private sealed class StyleDeclaration
    {
        public string ClassName = string.Empty;
        public string PropertiesText = string.Empty;
    }

    private sealed class AttribState
    {
        public string Id = string.Empty;
        public readonly float[] Xform = new float[6];
        public uint FillColor;
        public uint StrokeColor;
        public float Opacity = 1.0f;
        public float FillOpacity = 1.0f;
        public float StrokeOpacity = 1.0f;
        public string FillGradient = string.Empty;
        public string StrokeGradient = string.Empty;
        public float StrokeWidth = 1.0f;
        public float StrokeDashOffset;
        public float[] StrokeDashArray = new float[MaxDashes];
        public int StrokeDashCount;
        public NanoSvgLineJoin StrokeLineJoin = NanoSvgLineJoin.Miter;
        public NanoSvgLineCap StrokeLineCap = NanoSvgLineCap.Butt;
        public float MiterLimit = 4.0f;
        public NanoSvgFillRule FillRule = NanoSvgFillRule.NonZero;
        public float FontSize;
        public uint StopColor;
        public float StopOpacity = 1.0f;
        public float StopOffset;
        public int HasFill = 1;
        public int HasStroke;
        public bool Visible = true;
        public byte PaintOrder = EncodePaintOrder(PaintKind.Fill, PaintKind.Stroke, PaintKind.Markers);

        public AttribState Clone()
        {
            AttribState s = (AttribState)MemberwiseClone();
            s.StrokeDashArray = new float[MaxDashes];
            Array.Copy(StrokeDashArray, s.StrokeDashArray, MaxDashes);
            return s;
        }
    }

    private enum PaintKind
    {
        Fill = 0,
        Markers = 1,
        Stroke = 2,
    }

    private static byte EncodePaintOrder(PaintKind a, PaintKind b, PaintKind c)
    {
        return (byte)((int)a | ((int)b << 2) | ((int)c << 4));
    }

    private static byte ParsePaintOrder(string str)
    {
        return str switch
        {
            "normal" or "fill stroke markers" => EncodePaintOrder(PaintKind.Fill, PaintKind.Stroke, PaintKind.Markers),
            "fill markers stroke" => EncodePaintOrder(PaintKind.Fill, PaintKind.Markers, PaintKind.Stroke),
            "markers fill stroke" => EncodePaintOrder(PaintKind.Markers, PaintKind.Fill, PaintKind.Stroke),
            "markers stroke fill" => EncodePaintOrder(PaintKind.Markers, PaintKind.Stroke, PaintKind.Fill),
            "stroke fill markers" => EncodePaintOrder(PaintKind.Stroke, PaintKind.Fill, PaintKind.Markers),
            "stroke markers fill" => EncodePaintOrder(PaintKind.Stroke, PaintKind.Markers, PaintKind.Fill),
            _ => EncodePaintOrder(PaintKind.Fill, PaintKind.Stroke, PaintKind.Markers),
        };
    }

    private sealed class Parser
    {
        private readonly AttribState[] _attrs = new AttribState[MaxAttrs];
        private int _attrHead;
        private readonly List<float> _pts = [];
        private readonly List<NanoSvgPath> _plist = [];
        private readonly NanoSvgImage _image = new NanoSvgImage();
        private readonly List<StyleDeclaration> _styles = [];
        private readonly List<GradientData> _gradients = [];
        private float _viewMinX, _viewMinY, _viewWidth, _viewHeight;
        // PORT: the C code zero-initializes these, making the effective default
        // preserveAspectRatio="none"; the SVG spec default xMidYMid meet is used here.
        private int _alignX = AlignMid;
        private int _alignY = AlignMid;
        private int _alignType = AlignMeet;
        private readonly float _dpi;
        private bool _defsFlag;

        public NanoSvgImage Image => _image;

        public Parser(float dpi)
        {
            _dpi = dpi;
            AttribState root = new AttribState();
            XformIdentity(root.Xform);
            _attrs[0] = root;
        }

        public void StartElement(XElement el)
        {
            string name = el.Name.LocalName;

            if (_defsFlag)
            {
                // Skip everything but gradients and styles in defs.
                switch (name)
                {
                    case "linearGradient":
                        ParseGradient(el, NanoSvgPaintType.LinearGradient);
                        break;
                    case "radialGradient":
                        ParseGradient(el, NanoSvgPaintType.RadialGradient);
                        break;
                    case "stop":
                        ParseGradientStop(el);
                        break;
                }
                return;
            }

            switch (name)
            {
                case "g":
                    PushAttr();
                    ParseAttribs(el);
                    break;
                case "path":
                    PushAttr();
                    ParsePath(el);
                    PopAttr();
                    break;
                case "rect":
                    PushAttr();
                    ParseRect(el);
                    PopAttr();
                    break;
                case "circle":
                    PushAttr();
                    ParseCircle(el);
                    PopAttr();
                    break;
                case "ellipse":
                    PushAttr();
                    ParseEllipse(el);
                    PopAttr();
                    break;
                case "line":
                    PushAttr();
                    ParseLine(el);
                    PopAttr();
                    break;
                case "polyline":
                    PushAttr();
                    ParsePoly(el, false);
                    PopAttr();
                    break;
                case "polygon":
                    PushAttr();
                    ParsePoly(el, true);
                    PopAttr();
                    break;
                case "linearGradient":
                    ParseGradient(el, NanoSvgPaintType.LinearGradient);
                    break;
                case "radialGradient":
                    ParseGradient(el, NanoSvgPaintType.RadialGradient);
                    break;
                case "stop":
                    ParseGradientStop(el);
                    break;
                case "defs":
                    _defsFlag = true;
                    break;
                case "svg":
                    ParseSvg(el);
                    break;
                case "style":
                    ParseStyleSheet(el.Value);
                    break;
            }
        }

        public void EndElement(XElement el)
        {
            switch (el.Name.LocalName)
            {
                case "g":
                    PopAttr();
                    break;
                case "defs":
                    _defsFlag = false;
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Transform helpers (xform[6] = row-major 2x3 [a b c d e f]).
        // ------------------------------------------------------------------

        private static void XformIdentity(float[] t)
        {
            t[0] = 1.0f; t[1] = 0.0f;
            t[2] = 0.0f; t[3] = 1.0f;
            t[4] = 0.0f; t[5] = 0.0f;
        }

        private static void XformSetTranslation(float[] t, float tx, float ty)
        {
            t[0] = 1.0f; t[1] = 0.0f;
            t[2] = 0.0f; t[3] = 1.0f;
            t[4] = tx; t[5] = ty;
        }

        private static void XformSetScale(float[] t, float sx, float sy)
        {
            t[0] = sx; t[1] = 0.0f;
            t[2] = 0.0f; t[3] = sy;
            t[4] = 0.0f; t[5] = 0.0f;
        }

        private static void XformSetSkewX(float[] t, float a)
        {
            t[0] = 1.0f; t[1] = 0.0f;
            t[2] = MathF.Tan(a); t[3] = 1.0f;
            t[4] = 0.0f; t[5] = 0.0f;
        }

        private static void XformSetSkewY(float[] t, float a)
        {
            t[0] = 1.0f; t[1] = MathF.Tan(a);
            t[2] = 0.0f; t[3] = 1.0f;
            t[4] = 0.0f; t[5] = 0.0f;
        }

        private static void XformSetRotation(float[] t, float a)
        {
            float cs = MathF.Cos(a), sn = MathF.Sin(a);
            t[0] = cs; t[1] = sn;
            t[2] = -sn; t[3] = cs;
            t[4] = 0.0f; t[5] = 0.0f;
        }

        private static void XformMultiply(float[] t, float[] s)
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

        private static void XformInverse(float[] inv, float[] t)
        {
            double det = (double)t[0] * t[3] - (double)t[2] * t[1];
            if (det > -1e-6 && det < 1e-6)
            {
                // PORT: reset the inverse (the C code resets the source and leaves
                // the inverse uninitialized).
                XformIdentity(inv);
                return;
            }
            double invdet = 1.0 / det;
            inv[0] = (float)(t[3] * invdet);
            inv[2] = (float)(-t[2] * invdet);
            inv[4] = (float)(((double)t[2] * t[5] - (double)t[3] * t[4]) * invdet);
            inv[1] = (float)(-t[1] * invdet);
            inv[3] = (float)(t[0] * invdet);
            inv[5] = (float)(((double)t[1] * t[4] - (double)t[0] * t[5]) * invdet);
        }

        private static void XformPremultiply(float[] t, float[] s)
        {
            float[] s2 = (float[])s.Clone();
            XformMultiply(s2, t);
            Array.Copy(s2, t, 6);
        }

        private static void XformPoint(out float dx, out float dy, float x, float y, float[] t)
        {
            dx = x * t[0] + y * t[2] + t[4];
            dy = x * t[1] + y * t[3] + t[5];
        }

        private static void XformVec(out float dx, out float dy, float x, float y, float[] t)
        {
            dx = x * t[0] + y * t[2];
            dy = x * t[1] + y * t[3];
        }

        private static float GetAverageScale(float[] t)
        {
            float sx = MathF.Sqrt(t[0] * t[0] + t[2] * t[2]);
            float sy = MathF.Sqrt(t[1] * t[1] + t[3] * t[3]);
            return (sx + sy) * 0.5f;
        }

        // ------------------------------------------------------------------
        // Attribute stack.
        // ------------------------------------------------------------------

        private AttribState GetAttr()
        {
            return _attrs[_attrHead];
        }

        private void PushAttr()
        {
            if (_attrHead < MaxAttrs - 1)
            {
                _attrHead++;
                _attrs[_attrHead] = _attrs[_attrHead - 1].Clone();
            }
        }

        private void PopAttr()
        {
            if (_attrHead > 0)
            {
                _attrHead--;
            }
        }

        // ------------------------------------------------------------------
        // Number and unit parsing.
        // ------------------------------------------------------------------

        private static bool IsSpace(char c)
        {
            return c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';
        }

        private static bool IsDigit(char c)
        {
            return c is >= '0' and <= '9';
        }

        /// <summary>Extracts a number token into <paramref name="it"/> starting at
        /// <paramref name="s"/>; returns the index one past the token. Tokenizer
        /// equivalent of the C nsvg__parseNumber.</summary>
        private static int ParseNumber(ReadOnlySpan<char> str, int s, Span<char> it)
        {
            int last = it.Length - 1;
            int i = 0;
            int end = str.Length;

            // sign
            if (s < end && (str[s] == '-' || str[s] == '+'))
            {
                if (i < last) it[i++] = str[s];
                s++;
            }
            // integer part
            while (s < end && IsDigit(str[s]))
            {
                if (i < last) it[i++] = str[s];
                s++;
            }
            if (s < end && str[s] == '.')
            {
                if (i < last) it[i++] = str[s];
                s++;
                // fraction part
                while (s < end && IsDigit(str[s]))
                {
                    if (i < last) it[i++] = str[s];
                    s++;
                }
            }
            // exponent — the s[1] guard stops the tokenizer from eating 'em'/'ex' units.
            if (s < end && (str[s] == 'e' || str[s] == 'E') &&
                s + 1 < end && str[s + 1] != 'm' && str[s + 1] != 'x')
            {
                if (i < last) it[i++] = str[s];
                s++;
                if (s < end && (str[s] == '-' || str[s] == '+'))
                {
                    if (i < last) it[i++] = str[s];
                    s++;
                }
                while (s < end && IsDigit(str[s]))
                {
                    if (i < last) it[i++] = str[s];
                    s++;
                }
            }
            it[i] = '\0';
            return s;
        }

        private static int TokenLength(Span<char> it)
        {
            int n = 0;
            while (n < it.Length && it[n] != '\0')
            {
                n++;
            }
            return n;
        }

        /// <summary>Locale-independent float parse of a digit token; equivalent of
        /// the C nsvg__atof (which rolls its own for locale independence).</summary>
        private static float Atof(ReadOnlySpan<char> token)
        {
            int len = token.Length;
            int cur = 0;
            double sign = 1.0;

            if (cur < len && token[cur] == '+')
            {
                cur++;
            }
            else if (cur < len && token[cur] == '-')
            {
                sign = -1.0;
                cur++;
            }

            long intPart = 0;
            bool hasIntPart = false;
            while (cur < len && IsDigit(token[cur]))
            {
                intPart = intPart * 10 + (token[cur] - '0');
                cur++;
                hasIntPart = true;
            }

            double res = hasIntPart ? intPart : 0.0;
            bool hasFracPart = false;
            if (cur < len && token[cur] == '.')
            {
                cur++;
                long fracPart = 0;
                int digits = 0;
                while (cur < len && IsDigit(token[cur]))
                {
                    fracPart = fracPart * 10 + (token[cur] - '0');
                    digits++;
                    cur++;
                    hasFracPart = true;
                }
                if (hasFracPart)
                {
                    res += fracPart / Math.Pow(10.0, digits);
                }
            }

            if (!hasIntPart && !hasFracPart)
            {
                return 0.0f;
            }

            if (cur < len && (token[cur] == 'e' || token[cur] == 'E'))
            {
                cur++;
                double expSign = 1.0;
                if (cur < len && (token[cur] == '-' || token[cur] == '+'))
                {
                    if (token[cur] == '-')
                    {
                        expSign = -1.0;
                    }
                    cur++;
                }
                long expPart = 0;
                bool hasExp = false;
                while (cur < len && IsDigit(token[cur]))
                {
                    expPart = expPart * 10 + (token[cur] - '0');
                    cur++;
                    hasExp = true;
                }
                if (hasExp)
                {
                    res *= Math.Pow(10.0, expPart * expSign);
                }
            }

            return (float)(res * sign);
        }

        private static Units ParseUnits(ReadOnlySpan<char> units)
        {
            if (units.Length >= 2)
            {
                if (units[0] == 'p' && units[1] == 'x') return Units.Px;
                if (units[0] == 'p' && units[1] == 't') return Units.Pt;
                if (units[0] == 'p' && units[1] == 'c') return Units.Pc;
                if (units[0] == 'm' && units[1] == 'm') return Units.Mm;
                if (units[0] == 'c' && units[1] == 'm') return Units.Cm;
                if (units[0] == 'i' && units[1] == 'n') return Units.Inch;
                if (units[0] == 'e' && units[1] == 'm') return Units.Em;
                if (units[0] == 'e' && units[1] == 'x') return Units.Ex;
            }
            if (units.Length >= 1 && units[0] == '%') return Units.Percent;
            return Units.User;
        }

        private static Coordinate ParseCoordinateRaw(ReadOnlySpan<char> str)
        {
            Span<char> buf = stackalloc char[64];
            int next = ParseNumber(str, 0, buf);
            int n = TokenLength(buf);
            Units unit = ParseUnits(str[next..]);
            return new Coordinate(Atof(buf[..n]), unit);
        }

        private float ActualLength()
        {
            float w = _viewWidth, h = _viewHeight;
            return MathF.Sqrt(w * w + h * h) / MathF.Sqrt(2.0f);
        }

        private float ConvertToPixels(Coordinate c, float orig, float length)
        {
            switch (c.Unit)
            {
                case Units.User: return c.Value;
                case Units.Px: return c.Value;
                case Units.Pt: return c.Value / 72.0f * _dpi;
                case Units.Pc: return c.Value / 6.0f * _dpi;
                case Units.Mm: return c.Value / 25.4f * _dpi;
                case Units.Cm: return c.Value / 2.54f * _dpi;
                case Units.Inch: return c.Value * _dpi;
                case Units.Em: return c.Value * GetAttr().FontSize;
                case Units.Ex: return c.Value * GetAttr().FontSize * 0.52f; // x-height of Helvetica
                case Units.Percent: return orig + c.Value / 100.0f * length;
                default: return c.Value;
            }
        }

        private float ParseCoordinate(ReadOnlySpan<char> str, float orig, float length)
        {
            Coordinate coord = ParseCoordinateRaw(str);
            return ConvertToPixels(coord, orig, length);
        }

        // ------------------------------------------------------------------
        // Color parsing.
        // ------------------------------------------------------------------

        private static uint ParseColorHex(ReadOnlySpan<char> str)
        {
            if (str.Length >= 1 && str[0] == '#')
            {
                str = str[1..];
            }
            if (str.Length >= 6 &&
                Uri.IsHexDigit(str[0]) && Uri.IsHexDigit(str[1]) && Uri.IsHexDigit(str[2]) &&
                Uri.IsHexDigit(str[3]) && Uri.IsHexDigit(str[4]) && Uri.IsHexDigit(str[5]))
            {
                uint r = HexVal(str[0]) * 16u + HexVal(str[1]);
                uint g = HexVal(str[2]) * 16u + HexVal(str[3]);
                uint b = HexVal(str[4]) * 16u + HexVal(str[5]);
                return Rgb(r, g, b);
            }
            if (str.Length >= 3 && Uri.IsHexDigit(str[0]) && Uri.IsHexDigit(str[1]) && Uri.IsHexDigit(str[2]))
            {
                // 1 digit hex, e.g. #abc -> (a<<4|a), ...
                return Rgb(HexVal(str[0]) * 17u, HexVal(str[1]) * 17u, HexVal(str[2]) * 17u);
            }
            return Rgb(128, 128, 128);
        }

        private static uint HexVal(char c)
        {
            return c switch
            {
                >= '0' and <= '9' => (uint)(c - '0'),
                >= 'a' and <= 'f' => (uint)(c - 'a' + 10),
                >= 'A' and <= 'F' => (uint)(c - 'A' + 10),
                _ => 0,
            };
        }

        private static uint ParseColorRgb(ReadOnlySpan<char> str)
        {
            str = str[4..]; // skip "rgb("
            uint[] rgb = new uint[3];

            // Try decimal integers first: "rgb(%u, %u, %u)".
            int i = 0, pos = 0;
            bool intOk = true;
            for (i = 0; i < 3; i++)
            {
                while (pos < str.Length && IsSpace(str[pos])) pos++;
                if (pos < str.Length && str[pos] == '+') pos++;
                long v = 0;
                bool has = false;
                while (pos < str.Length && IsDigit(str[pos]))
                {
                    v = v * 10 + (str[pos] - '0');
                    if (v > int.MaxValue) v = int.MaxValue;
                    pos++;
                    has = true;
                }
                if (!has)
                {
                    intOk = false;
                    break;
                }
                rgb[i] = (uint)v;
                while (pos < str.Length && IsSpace(str[pos])) pos++;
                if (i < 2)
                {
                    if (pos >= str.Length || str[pos] != ',')
                    {
                        intOk = false;
                        break;
                    }
                    pos++;
                }
                else if (pos < str.Length && str[pos] == ')')
                {
                    pos++;
                }
            }
            if (!intOk)
            {
                // Integers failed; try percent values (locale independent).
                pos = 0;
                i = 0;
                while (i < 3 && pos < str.Length)
                {
                    while (pos < str.Length && IsSpace(str[pos])) pos++;
                    if (pos < str.Length && str[pos] == '+') pos++;
                    if (pos >= str.Length) break;
                    // parse a float
                    int start = pos;
                    while (pos < str.Length && IsDigit(str[pos])) pos++;
                    if (pos < str.Length && str[pos] == '.')
                    {
                        pos++;
                        if (pos >= str.Length || !IsDigit(str[pos])) break;
                        while (pos < str.Length && IsDigit(str[pos])) pos++;
                    }
                    float v = Atof(str[start..pos]);
                    if (pos < str.Length && str[pos] == '%') pos++;
                    else break;
                    while (pos < str.Length && IsSpace(str[pos])) pos++;
                    char delimiter = i == 2 ? ')' : ',';
                    if (pos < str.Length && str[pos] == delimiter) pos++;
                    else break;
                    rgb[i] = (uint)MathF.Round(v * 2.55f);
                    i++;
                }
                if (i != 3)
                {
                    rgb[0] = rgb[1] = rgb[2] = 128;
                }
            }

            for (i = 0; i < 3; i++)
            {
                if (rgb[i] > 255) rgb[i] = 255;
            }
            return Rgb(rgb[0], rgb[1], rgb[2]);
        }

        private static uint ParseColorName(string str)
        {
            return NamedColors.TryGetValue(str, out uint color) ? color : Rgb(128, 128, 128);
        }

        private static uint ParseColor(ReadOnlySpan<char> str)
        {
            str = str.TrimStart(' ');
            if (str.Length >= 1 && str[0] == '#')
            {
                return ParseColorHex(str);
            }
            if (str.Length >= 4 && str[0] == 'r' && str[1] == 'g' && str[2] == 'b' && str[3] == '(')
            {
                return ParseColorRgb(str);
            }
            return ParseColorName(str.ToString());
        }

        private static float ParseOpacity(ReadOnlySpan<char> str)
        {
            float val = Atof(str);
            if (val < 0.0f) val = 0.0f;
            if (val > 1.0f) val = 1.0f;
            return val;
        }

        // ------------------------------------------------------------------
        // Style and attribute parsing.
        // ------------------------------------------------------------------

        private void ParseStyleSheet(string text)
        {
            // Only simple class selectors are supported ('.foo, .bar { ... }').
            int s = 0;
            int n = text.Length;
            while (s < n)
            {
                StyleDeclaration[] styles = new StyleDeclaration[MaxClasses];
                int nstyles = 0;

                // 1) Parse the selector list up to '{'.
                while (s < n && text[s] != '{')
                {
                    while (s < n && (IsSpace(text[s]) || text[s] == ',')) s++;
                    if (s >= n || text[s] == '{') break;

                    int selStart = s;
                    while (s < n && !IsSpace(text[s]) && text[s] != ',' && text[s] != '{') s++;
                    int selEnd = s;

                    if (text[selStart] != '.' || nstyles >= MaxClasses)
                    {
                        continue; // unsupported selector, or staging array full
                    }
                    selStart++;

                    styles[nstyles++] = new StyleDeclaration
                    {
                        ClassName = text.Substring(selStart, selEnd - selStart),
                    };
                }
                if (s >= n)
                {
                    break; // no '{' found
                }
                s++; // advance past '{'

                // 2) Properties block up to '}'.
                int propsStart = s;
                while (s < n && text[s] != '}') s++;
                int propsEnd = s;

                // 3) Commit to the style list (head insertion, like the C code).
                for (int i = 0; i < nstyles; i++)
                {
                    styles[i].PropertiesText = text.Substring(propsStart, propsEnd - propsStart);
                    _styles.Insert(0, styles[i]);
                }

                if (s < n) s++; // advance past '}'
            }
        }

        private void ApplyClassStyles(string value)
        {
            int cur = 0, n = value.Length;
            while (cur < n)
            {
                while (cur < n && IsSpace(value[cur])) cur++;
                if (cur >= n) break;

                int classStart = cur;
                while (cur < n && !IsSpace(value[cur])) cur++;

                string className = value.Substring(classStart, cur - classStart);
                foreach (StyleDeclaration style in _styles)
                {
                    if (style.ClassName == className)
                    {
                        ParseStyle(style.PropertiesText);
                    }
                }
            }
        }

        private bool ParseAttr(string name, string value)
        {
            AttribState attr = GetAttr();

            switch (name)
            {
                case "style":
                    ParseStyle(value);
                    return true;
                case "display":
                    if (value == "none") attr.Visible = false;
                    // Don't reset Visible on display:inline: one display:none hides the whole subtree.
                    return true;
                case "fill":
                    if (value == "none")
                    {
                        attr.HasFill = 0;
                    }
                    else if (value.StartsWith("url(", StringComparison.Ordinal))
                    {
                        attr.HasFill = 2;
                        attr.FillGradient = ParseUrl(value);
                    }
                    else
                    {
                        attr.HasFill = 1;
                        attr.FillColor = ParseColor(value);
                    }
                    return true;
                case "opacity":
                    attr.Opacity = ParseOpacity(value);
                    return true;
                case "fill-opacity":
                    attr.FillOpacity = ParseOpacity(value);
                    return true;
                case "stroke":
                    if (value == "none")
                    {
                        attr.HasStroke = 0;
                    }
                    else if (value.StartsWith("url(", StringComparison.Ordinal))
                    {
                        attr.HasStroke = 2;
                        attr.StrokeGradient = ParseUrl(value);
                    }
                    else
                    {
                        attr.HasStroke = 1;
                        attr.StrokeColor = ParseColor(value);
                    }
                    return true;
                case "stroke-width":
                    attr.StrokeWidth = ParseCoordinate(value, 0.0f, ActualLength());
                    return true;
                case "stroke-dasharray":
                    attr.StrokeDashCount = ParseStrokeDashArray(value, attr.StrokeDashArray);
                    return true;
                case "stroke-dashoffset":
                    attr.StrokeDashOffset = ParseCoordinate(value, 0.0f, ActualLength());
                    return true;
                case "stroke-opacity":
                    attr.StrokeOpacity = ParseOpacity(value);
                    return true;
                case "stroke-linecap":
                    attr.StrokeLineCap = value switch
                    {
                        "butt" => NanoSvgLineCap.Butt,
                        "round" => NanoSvgLineCap.Round,
                        "square" => NanoSvgLineCap.Square,
                        _ => NanoSvgLineCap.Butt,
                    };
                    return true;
                case "stroke-linejoin":
                    attr.StrokeLineJoin = value switch
                    {
                        "miter" => NanoSvgLineJoin.Miter,
                        "round" => NanoSvgLineJoin.Round,
                        "bevel" => NanoSvgLineJoin.Bevel,
                        _ => NanoSvgLineJoin.Miter,
                    };
                    return true;
                case "stroke-miterlimit":
                    attr.MiterLimit = MathF.Max(0.0f, Atof(value));
                    return true;
                case "fill-rule":
                    attr.FillRule = value == "evenodd" ? NanoSvgFillRule.EvenOdd : NanoSvgFillRule.NonZero;
                    return true;
                case "font-size":
                    attr.FontSize = ParseCoordinate(value, 0.0f, ActualLength());
                    return true;
                case "transform":
                {
                    float[] xform = new float[6];
                    ParseTransform(xform, value);
                    XformPremultiply(attr.Xform, xform);
                    return true;
                }
                case "stop-color":
                    attr.StopColor = ParseColor(value);
                    return true;
                case "stop-opacity":
                    attr.StopOpacity = ParseOpacity(value);
                    return true;
                case "offset":
                    attr.StopOffset = ParseCoordinate(value, 0.0f, 1.0f);
                    return true;
                case "paint-order":
                    attr.PaintOrder = ParsePaintOrder(value);
                    return true;
                case "id":
                    attr.Id = value;
                    return true;
                case "class":
                    ApplyClassStyles(value);
                    return true;
                default:
                    return false;
            }
        }

        private static string ParseUrl(string str)
        {
            // "url(#id)"
            int start = 4;
            if (start < str.Length && str[start] == '#') start++;
            int end = str.IndexOf(')', start);
            return end < 0 ? str[start..] : str[start..end];
        }

        private int ParseStrokeDashArray(string str, float[] strokeDashArray)
        {
            int count = 0;

            // Handle "none".
            if (str.StartsWith('n'))
            {
                return 0;
            }

            int pos = 0;
            int n = str.Length;
            Span<char> item = stackalloc char[64];
            while (pos < n)
            {
                pos = GetNextDashItem(str, pos, item);
                int len = TokenLength(item);
                if (len == 0) break;
                if (count < MaxDashes)
                {
                    strokeDashArray[count++] = MathF.Abs(ParseCoordinate(item[..len], 0.0f,
                        ActualLength()));
                }
            }

            float sum = 0.0f;
            for (int i = 0; i < count; i++)
            {
                sum += strokeDashArray[i];
            }
            if (sum <= 1e-6f)
            {
                count = 0;
            }

            return count;
        }

        private static int GetNextDashItem(ReadOnlySpan<char> s, int pos, Span<char> it)
        {
            int n = 0;
            int len = s.Length;
            it[0] = '\0';
            while (pos < len && (IsSpace(s[pos]) || s[pos] == ',')) pos++;
            while (pos < len && !IsSpace(s[pos]) && s[pos] != ',')
            {
                if (n < it.Length - 1) it[n++] = s[pos];
                pos++;
            }
            it[n] = '\0';
            return pos;
        }

        private void ParseNameValue(ReadOnlySpan<char> decl)
        {
            int colon = decl.IndexOf(':');
            if (colon < 0)
            {
                return;
            }
            string name = decl[..colon].TrimEnd(": \t\n\v\f\r".AsSpan()).ToString();
            ReadOnlySpan<char> valueSpan = decl[(colon + 1)..];
            int valueStart = 0;
            while (valueStart < valueSpan.Length && (valueSpan[valueStart] == ':' || IsSpace(valueSpan[valueStart]))) valueStart++;
            string value = valueSpan[valueStart..].TrimEnd(" \t\n\v\f\r".AsSpan()).ToString();
            ParseAttr(name, value);
        }

        private void ParseStyle(string str)
        {
            int pos = 0;
            int n = str.Length;
            while (pos < n)
            {
                while (pos < n && IsSpace(str[pos])) pos++;
                int start = pos;
                while (pos < n && str[pos] != ';') pos++;
                int end = pos;
                while (end > start && (str[end - 1] == ';' || IsSpace(str[end - 1]))) end--;
                if (end > start)
                {
                    ParseNameValue(str.AsSpan(start, end - start));
                }
                if (pos < n) pos++;
            }
        }

        private void ParseAttribs(XElement el)
        {
            foreach (XAttribute a in el.Attributes())
            {
                if (a.Name.LocalName == "style")
                {
                    ParseStyle(a.Value);
                }
                else
                {
                    ParseAttr(a.Name.LocalName, a.Value);
                }
            }
        }

        // ------------------------------------------------------------------
        // Transform attribute parsing.
        // ------------------------------------------------------------------

        /// <summary>Parses the number list of one transform function; returns the
        /// index just past the closing ')' or -1 when malformed.</summary>
        private static int ParseTransformArgs(ReadOnlySpan<char> str, float[] args, int maxNa, out int na)
        {
            na = 0;
            int ptr = 0;
            int len = str.Length;
            while (ptr < len && str[ptr] != '(') ptr++;
            if (ptr >= len) return -1;
            int open = ptr;
            int end = ptr;
            while (end < len && str[end] != ')') end++;
            if (end >= len) return -1;

            Span<char> it = stackalloc char[64];
            ptr++;
            while (ptr < end)
            {
                char c = str[ptr];
                if (c == '-' || c == '+' || c == '.' || IsDigit(c))
                {
                    if (na >= maxNa) return -1;
                    ptr = ParseNumber(str, ptr, it);
                    args[na++] = Atof(it[..TokenLength(it)]);
                }
                else
                {
                    ptr++;
                }
            }
            return end + 1;
        }

        private static void ParseTransform(float[] xform, ReadOnlySpan<char> str)
        {
            float[] t = new float[6];
            XformIdentity(xform);
            int pos = 0;
            int len = str.Length;
            while (pos < len)
            {
                int consumed;
                bool ok;
                if (str[pos..].StartsWith("matrix", StringComparison.Ordinal))
                {
                    ok = ParseMatrix(t, str[pos..], out consumed);
                }
                else if (str[pos..].StartsWith("translate", StringComparison.Ordinal))
                {
                    ok = ParseTranslate(t, str[pos..], out consumed);
                }
                else if (str[pos..].StartsWith("scale", StringComparison.Ordinal))
                {
                    ok = ParseScale(t, str[pos..], out consumed);
                }
                else if (str[pos..].StartsWith("rotate", StringComparison.Ordinal))
                {
                    ok = ParseRotate(t, str[pos..], out consumed);
                }
                else if (str[pos..].StartsWith("skewX", StringComparison.Ordinal))
                {
                    ok = ParseSkewX(t, str[pos..], out consumed);
                }
                else if (str[pos..].StartsWith("skewY", StringComparison.Ordinal))
                {
                    ok = ParseSkewY(t, str[pos..], out consumed);
                }
                else
                {
                    pos++;
                    continue;
                }

                // PORT: the C code premultiplies even when the argument list is
                // malformed (with uninitialized data); here a malformed function
                // is skipped without touching xform.
                if (consumed > 0)
                {
                    pos += consumed;
                }
                else
                {
                    pos++;
                    continue;
                }
                if (ok)
                {
                    XformPremultiply(xform, t);
                }
            }
        }

        private static bool ParseMatrix(float[] xform, ReadOnlySpan<char> str, out int consumed)
        {
            float[] t = new float[6];
            int len = ParseTransformArgs(str, t, 6, out int na);
            consumed = len;
            if (na != 6) return false;
            Array.Copy(t, xform, 6);
            return true;
        }

        private static bool ParseTranslate(float[] xform, ReadOnlySpan<char> str, out int consumed)
        {
            float[] args = new float[2];
            int len = ParseTransformArgs(str, args, 2, out int na);
            consumed = len;
            if (na == 1) args[1] = 0.0f;
            if (na < 1) return false;
            XformSetTranslation(xform, args[0], args[1]);
            return true;
        }

        private static bool ParseScale(float[] xform, ReadOnlySpan<char> str, out int consumed)
        {
            float[] args = new float[2];
            int len = ParseTransformArgs(str, args, 2, out int na);
            consumed = len;
            if (na == 1) args[1] = args[0];
            if (na < 1) return false;
            XformSetScale(xform, args[0], args[1]);
            return true;
        }

        private static bool ParseSkewX(float[] xform, ReadOnlySpan<char> str, out int consumed)
        {
            float[] args = new float[1];
            int len = ParseTransformArgs(str, args, 1, out int na);
            consumed = len;
            if (na < 1) return false;
            XformSetSkewX(xform, args[0] / 180.0f * Pi);
            return true;
        }

        private static bool ParseSkewY(float[] xform, ReadOnlySpan<char> str, out int consumed)
        {
            float[] args = new float[1];
            int len = ParseTransformArgs(str, args, 1, out int na);
            consumed = len;
            if (na < 1) return false;
            XformSetSkewY(xform, args[0] / 180.0f * Pi);
            return true;
        }

        private static bool ParseRotate(float[] xform, ReadOnlySpan<char> str, out int consumed)
        {
            float[] args = new float[3];
            float[] m = new float[6];
            float[] t = new float[6];
            int len = ParseTransformArgs(str, args, 3, out int na);
            consumed = len;
            if (na < 1) return false;
            if (na == 1) args[1] = args[2] = 0.0f;
            XformIdentity(m);

            if (na > 1)
            {
                XformSetTranslation(t, -args[1], -args[2]);
                XformMultiply(m, t);
            }

            XformSetRotation(t, args[0] / 180.0f * Pi);
            XformMultiply(m, t);

            if (na > 1)
            {
                XformSetTranslation(t, args[1], args[2]);
                XformMultiply(m, t);
            }

            Array.Copy(m, xform, 6);
            return true;
        }

        // ------------------------------------------------------------------
        // Bezier bounds.
        // ------------------------------------------------------------------

        private const double Epsilon = 1e-12;

        private static bool PtInBounds(float[] pt, float[] bounds)
        {
            return pt[0] >= bounds[0] && pt[0] <= bounds[2] && pt[1] >= bounds[1] && pt[1] <= bounds[3];
        }

        private static double EvalBezier(double t, double p0, double p1, double p2, double p3)
        {
            double it = 1.0 - t;
            return it * it * it * p0 + 3.0 * it * it * t * p1 + 3.0 * it * t * t * p2 + t * t * t * p3;
        }

        private static void CurveBounds(float[] bounds, ReadOnlySpan<float> curve)
        {
            double[] roots = new double[2];
            float[] v0 = [curve[0], curve[1]];
            float[] v1 = [curve[2], curve[3]];
            float[] v2 = [curve[4], curve[5]];
            float[] v3 = [curve[6], curve[7]];

            // Start the bounding box by end points.
            bounds[0] = MathF.Min(v0[0], v3[0]);
            bounds[1] = MathF.Min(v0[1], v3[1]);
            bounds[2] = MathF.Max(v0[0], v3[0]);
            bounds[3] = MathF.Max(v0[1], v3[1]);

            // The curve fits inside the convex hull of its control points.
            if (PtInBounds(v1, bounds) && PtInBounds(v2, bounds))
            {
                return;
            }

            // Add bezier curve inflection points in X and Y.
            for (int i = 0; i < 2; i++)
            {
                double a = -3.0 * v0[i] + 9.0 * v1[i] - 9.0 * v2[i] + 3.0 * v3[i];
                double b = 6.0 * v0[i] - 12.0 * v1[i] + 6.0 * v2[i];
                double c = 3.0 * v1[i] - 3.0 * v0[i];
                int count = 0;
                if (MathF.Abs((float)a) < Epsilon)
                {
                    if (MathF.Abs((float)b) > Epsilon)
                    {
                        double t = -c / b;
                        if (t > Epsilon && t < 1.0 - Epsilon)
                        {
                            roots[count++] = t;
                        }
                    }
                }
                else
                {
                    double b2ac = b * b - 4.0 * c * a;
                    if (b2ac > Epsilon)
                    {
                        double t = (-b + Math.Sqrt(b2ac)) / (2.0 * a);
                        if (t > Epsilon && t < 1.0 - Epsilon)
                        {
                            roots[count++] = t;
                        }
                        t = (-b - Math.Sqrt(b2ac)) / (2.0 * a);
                        if (t > Epsilon && t < 1.0 - Epsilon)
                        {
                            roots[count++] = t;
                        }
                    }
                }
                for (int j = 0; j < count; j++)
                {
                    double v = EvalBezier(roots[j], v0[i], v1[i], v2[i], v3[i]);
                    bounds[i] = MathF.Min(bounds[i], (float)v);
                    bounds[2 + i] = MathF.Max(bounds[2 + i], (float)v);
                }
            }
        }

        // ------------------------------------------------------------------
        // Path building during element parsing.
        // ------------------------------------------------------------------

        private void ResetPath()
        {
            _pts.Clear();
        }

        private void AddPoint(float x, float y)
        {
            _pts.Add(x);
            _pts.Add(y);
        }

        private void MoveTo(float x, float y)
        {
            if (_pts.Count > 0)
            {
                _pts[^2] = x;
                _pts[^1] = y;
            }
            else
            {
                AddPoint(x, y);
            }
        }

        private void LineTo(float x, float y)
        {
            if (_pts.Count > 0)
            {
                float px = _pts[^2];
                float py = _pts[^1];
                float dx = x - px;
                float dy = y - py;
                AddPoint(px + dx / 3.0f, py + dy / 3.0f);
                AddPoint(x - dx / 3.0f, y - dy / 3.0f);
                AddPoint(x, y);
            }
        }

        private void CubicBezTo(float cpx1, float cpy1, float cpx2, float cpy2, float x, float y)
        {
            if (_pts.Count > 0)
            {
                AddPoint(cpx1, cpy1);
                AddPoint(cpx2, cpy2);
                AddPoint(x, y);
            }
        }

        private void AddPath(bool closed)
        {
            if (closed && _pts.Count > 0)
            {
                LineTo(_pts[0], _pts[1]);
            }

            // Expect 1 + N*3 points (N = number of cubic bezier segments).
            if (_pts.Count < 4 || (_pts.Count / 2 % 3) != 1)
            {
                return;
            }

            NanoSvgPath path = new NanoSvgPath
            {
                Pts = _pts.ToArray(),
                Closed = closed,
            };

            // Transform path.
            AttribState attr = GetAttr();
            int npts = path.Pts.Length / 2;
            for (int i = 0; i < npts; ++i)
            {
                XformPoint(out float dx, out float dy, path.Pts[i * 2], path.Pts[i * 2 + 1], attr.Xform);
                path.Pts[i * 2] = dx;
                path.Pts[i * 2 + 1] = dy;
            }

            // Find bounds.
            float[] bounds = new float[4];
            for (int i = 0; i < npts - 1; i += 3)
            {
                CurveBounds(bounds, path.Pts.AsSpan(i * 2, 8));
                if (i == 0)
                {
                    Array.Copy(bounds, path.Bounds, 4);
                }
                else
                {
                    path.Bounds[0] = MathF.Min(path.Bounds[0], bounds[0]);
                    path.Bounds[1] = MathF.Min(path.Bounds[1], bounds[1]);
                    path.Bounds[2] = MathF.Max(path.Bounds[2], bounds[2]);
                    path.Bounds[3] = MathF.Max(path.Bounds[3], bounds[3]);
                }
            }

            // PORT: document order instead of the C code's head insertion.
            _plist.Add(path);
        }

        private void AddShape()
        {
            AttribState attr = GetAttr();

            if (_plist.Count == 0)
            {
                return;
            }

            NanoSvgShape shape = new NanoSvgShape
            {
                Id = attr.Id,
                FillGradientId = attr.FillGradient,
                StrokeGradientId = attr.StrokeGradient,
                Xform = (float[])attr.Xform.Clone(),
                StrokeWidth = attr.StrokeWidth,
                StrokeDashOffset = attr.StrokeDashOffset,
                StrokeDashArray = attr.StrokeDashArray[..attr.StrokeDashCount],
                StrokeLineJoin = attr.StrokeLineJoin,
                StrokeLineCap = attr.StrokeLineCap,
                MiterLimit = attr.MiterLimit,
                FillRule = attr.FillRule,
                Opacity = attr.Opacity,
                PaintOrder = attr.PaintOrder,
                Fill = new NanoSvgPaint(NanoSvgPaintType.None),
                Stroke = new NanoSvgPaint(NanoSvgPaintType.None),
            };

            float scale = GetAverageScale(attr.Xform);
            shape.StrokeWidth *= scale;
            shape.StrokeDashOffset *= scale;
            for (int i = 0; i < shape.StrokeDashArray.Length; i++)
            {
                shape.StrokeDashArray[i] *= scale;
            }

            shape.Paths.AddRange(_plist);
            _plist.Clear();

            // Calculate shape bounds.
            Array.Copy(shape.Paths[0].Bounds, shape.Bounds, 4);
            for (int i = 1; i < shape.Paths.Count; i++)
            {
                float[] b = shape.Paths[i].Bounds;
                shape.Bounds[0] = MathF.Min(shape.Bounds[0], b[0]);
                shape.Bounds[1] = MathF.Min(shape.Bounds[1], b[1]);
                shape.Bounds[2] = MathF.Max(shape.Bounds[2], b[2]);
                shape.Bounds[3] = MathF.Max(shape.Bounds[3], b[3]);
            }

            // Set fill.
            NanoSvgPaint fill = shape.Fill;
            if (attr.HasFill == 0)
            {
                fill.Type = NanoSvgPaintType.None;
            }
            else if (attr.HasFill == 1)
            {
                fill.Type = NanoSvgPaintType.Color;
                fill.Color = attr.FillColor | ((uint)(attr.FillOpacity * 255) << 24);
            }
            else if (attr.HasFill == 2)
            {
                fill.Type = NanoSvgPaintType.Undefined;
            }
            shape.Fill = fill;

            // Set stroke.
            NanoSvgPaint stroke = shape.Stroke;
            if (attr.HasStroke == 0)
            {
                stroke.Type = NanoSvgPaintType.None;
            }
            else if (attr.HasStroke == 1)
            {
                stroke.Type = NanoSvgPaintType.Color;
                stroke.Color = attr.StrokeColor | ((uint)(attr.StrokeOpacity * 255) << 24);
            }
            else if (attr.HasStroke == 2)
            {
                stroke.Type = NanoSvgPaintType.Undefined;
            }
            shape.Stroke = stroke;

            // Set flags.
            shape.Flags = attr.Visible ? NanoSvgShapeFlags.Visible : 0;

            _image.Shapes.Add(shape);
        }

        // ------------------------------------------------------------------
        // Path data ("d" attribute) parsing.
        // ------------------------------------------------------------------

        private static bool IsCoordinate(ReadOnlySpan<char> s)
        {
            if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
            {
                s = s[1..];
            }
            return s.Length > 0 && (IsDigit(s[0]) || s[0] == '.');
        }

        private static int GetArgsPerElement(char cmd)
        {
            return cmd switch
            {
                'v' or 'V' or 'h' or 'H' => 1,
                'm' or 'M' or 'l' or 'L' or 't' or 'T' => 2,
                'q' or 'Q' or 's' or 'S' => 4,
                'c' or 'C' => 6,
                'a' or 'A' => 7,
                'z' or 'Z' => 0,
                _ => -1,
            };
        }

        /// <summary>Reads the next path token at <paramref name="pos"/>; number or single
        /// command char. Returns the new position; token length 0 means end of input.</summary>
        private static int GetNextPathItem(ReadOnlySpan<char> s, int pos, Span<char> it)
        {
            it[0] = '\0';
            int len = s.Length;
            while (pos < len && (IsSpace(s[pos]) || s[pos] == ',')) pos++;
            if (pos >= len) return pos;
            char c = s[pos];
            if (c == '-' || c == '+' || c == '.' || IsDigit(c))
            {
                return ParseNumber(s, pos, it);
            }
            it[0] = c;
            it[1] = '\0';
            return pos + 1;
        }

        /// <summary>Reads a single-character arc flag '0' or '1'.</summary>
        private static int GetNextPathItemWhenArcFlag(ReadOnlySpan<char> s, int pos, Span<char> it)
        {
            it[0] = '\0';
            int len = s.Length;
            while (pos < len && (IsSpace(s[pos]) || s[pos] == ',')) pos++;
            if (pos >= len) return pos;
            if (s[pos] == '0' || s[pos] == '1')
            {
                it[0] = s[pos++];
                it[1] = '\0';
                return pos;
            }
            return pos;
        }

        private void PathMoveTo(ref float cpx, ref float cpy, float[] args, bool rel)
        {
            if (rel)
            {
                cpx += args[0];
                cpy += args[1];
            }
            else
            {
                cpx = args[0];
                cpy = args[1];
            }
            MoveTo(cpx, cpy);
        }

        private void PathLineTo(ref float cpx, ref float cpy, float[] args, bool rel)
        {
            if (rel)
            {
                cpx += args[0];
                cpy += args[1];
            }
            else
            {
                cpx = args[0];
                cpy = args[1];
            }
            LineTo(cpx, cpy);
        }

        private void PathHLineTo(ref float cpx, ref float cpy, float[] args, bool rel)
        {
            if (rel)
            {
                cpx += args[0];
            }
            else
            {
                cpx = args[0];
            }
            LineTo(cpx, cpy);
        }

        private void PathVLineTo(ref float cpx, ref float cpy, float[] args, bool rel)
        {
            if (rel)
            {
                cpy += args[0];
            }
            else
            {
                cpy = args[0];
            }
            LineTo(cpx, cpy);
        }

        private void PathCubicBezTo(ref float cpx, ref float cpy, ref float cpx2, ref float cpy2, float[] args, bool rel)
        {
            float cx1, cy1, cx2, cy2, x2, y2;
            if (rel)
            {
                cx1 = cpx + args[0];
                cy1 = cpy + args[1];
                cx2 = cpx + args[2];
                cy2 = cpy + args[3];
                x2 = cpx + args[4];
                y2 = cpy + args[5];
            }
            else
            {
                cx1 = args[0];
                cy1 = args[1];
                cx2 = args[2];
                cy2 = args[3];
                x2 = args[4];
                y2 = args[5];
            }

            CubicBezTo(cx1, cy1, cx2, cy2, x2, y2);

            cpx2 = cx2;
            cpy2 = cy2;
            cpx = x2;
            cpy = y2;
        }

        private void PathCubicBezShortTo(ref float cpx, ref float cpy, ref float cpx2, ref float cpy2, float[] args, bool rel)
        {
            float x1 = cpx, y1 = cpy;
            float cx2, cy2, x2, y2;
            if (rel)
            {
                cx2 = cpx + args[0];
                cy2 = cpy + args[1];
                x2 = cpx + args[2];
                y2 = cpy + args[3];
            }
            else
            {
                cx2 = args[0];
                cy2 = args[1];
                x2 = args[2];
                y2 = args[3];
            }

            float cx1 = 2 * x1 - cpx2;
            float cy1 = 2 * y1 - cpy2;

            CubicBezTo(cx1, cy1, cx2, cy2, x2, y2);

            cpx2 = cx2;
            cpy2 = cy2;
            cpx = x2;
            cpy = y2;
        }

        private void PathQuadBezTo(ref float cpx, ref float cpy, ref float cpx2, ref float cpy2, float[] args, bool rel)
        {
            float x1 = cpx, y1 = cpy;
            float cx, cy, x2, y2;
            if (rel)
            {
                cx = cpx + args[0];
                cy = cpy + args[1];
                x2 = cpx + args[2];
                y2 = cpy + args[3];
            }
            else
            {
                cx = args[0];
                cy = args[1];
                x2 = args[2];
                y2 = args[3];
            }

            // Convert to cubic bezier.
            float cx1 = x1 + 2.0f / 3.0f * (cx - x1);
            float cy1 = y1 + 2.0f / 3.0f * (cy - y1);
            float cx2 = x2 + 2.0f / 3.0f * (cx - x2);
            float cy2 = y2 + 2.0f / 3.0f * (cy - y2);

            CubicBezTo(cx1, cy1, cx2, cy2, x2, y2);

            cpx2 = cx;
            cpy2 = cy;
            cpx = x2;
            cpy = y2;
        }

        private void PathQuadBezShortTo(ref float cpx, ref float cpy, ref float cpx2, ref float cpy2, float[] args, bool rel)
        {
            float x1 = cpx, y1 = cpy;
            float x2, y2;
            if (rel)
            {
                x2 = cpx + args[0];
                y2 = cpy + args[1];
            }
            else
            {
                x2 = args[0];
                y2 = args[1];
            }

            float cx = 2 * x1 - cpx2;
            float cy = 2 * y1 - cpy2;

            // Convert to cubic bezier.
            float cx1 = x1 + 2.0f / 3.0f * (cx - x1);
            float cy1 = y1 + 2.0f / 3.0f * (cy - y1);
            float cx2 = x2 + 2.0f / 3.0f * (cx - x2);
            float cy2 = y2 + 2.0f / 3.0f * (cy - y2);

            CubicBezTo(cx1, cy1, cx2, cy2, x2, y2);

            cpx2 = cx;
            cpy2 = cy;
            cpx = x2;
            cpy = y2;
        }

        private static float Sqr(float x) => x * x;

        private static float VecRat(float ux, float uy, float vx, float vy)
        {
            return (ux * vx + uy * vy) / (MathF.Sqrt(ux * ux + uy * uy) * MathF.Sqrt(vx * vx + vy * vy));
        }

        private static float VecAng(float ux, float uy, float vx, float vy)
        {
            float r = VecRat(ux, uy, vx, vy);
            if (r < -1.0f) r = -1.0f;
            if (r > 1.0f) r = 1.0f;
            return ((ux * vy < uy * vx) ? -1.0f : 1.0f) * MathF.Acos(r);
        }

        private void PathArcTo(ref float cpx, ref float cpy, float[] args, bool rel)
        {
            // Ported from canvg (https://code.google.com/p/canvg/).
            float rx = MathF.Abs(args[0]);
            float ry = MathF.Abs(args[1]);
            float rotx = args[2] / 180.0f * Pi;
            int fa = MathF.Abs(args[3]) > 1e-6f ? 1 : 0;
            int fs = MathF.Abs(args[4]) > 1e-6f ? 1 : 0;
            float x1 = cpx;
            float y1 = cpy;
            float x2, y2;
            if (rel)
            {
                x2 = cpx + args[5];
                y2 = cpy + args[6];
            }
            else
            {
                x2 = args[5];
                y2 = args[6];
            }

            float dx = x1 - x2;
            float dy = y1 - y2;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            if (d < 1e-6f || rx < 1e-6f || ry < 1e-6f)
            {
                // The arc degenerates to a line.
                LineTo(x2, y2);
                cpx = x2;
                cpy = y2;
                return;
            }

            float sinrx = MathF.Sin(rotx);
            float cosrx = MathF.Cos(rotx);

            // Convert to center point parameterization.
            // http://www.w3.org/TR/SVG11/implnote.html#ArcImplementationNotes
            // 1) Compute x1', y1'
            float x1p = cosrx * dx / 2.0f + sinrx * dy / 2.0f;
            float y1p = -sinrx * dx / 2.0f + cosrx * dy / 2.0f;
            d = Sqr(x1p) / Sqr(rx) + Sqr(y1p) / Sqr(ry);
            if (d > 1)
            {
                d = MathF.Sqrt(d);
                rx *= d;
                ry *= d;
            }
            // 2) Compute cx', cy'
            float s = 0.0f;
            float sa = Sqr(rx) * Sqr(ry) - Sqr(rx) * Sqr(y1p) - Sqr(ry) * Sqr(x1p);
            float sb = Sqr(rx) * Sqr(y1p) + Sqr(ry) * Sqr(x1p);
            if (sa < 0.0f) sa = 0.0f;
            if (sb > 0.0f)
            {
                s = MathF.Sqrt(sa / sb);
            }
            if (fa == fs)
            {
                s = -s;
            }
            float cxp = s * rx * y1p / ry;
            float cyp = s * -ry * x1p / rx;

            // 3) Compute cx,cy from cx',cy'
            float cx = (x1 + x2) / 2.0f + cosrx * cxp - sinrx * cyp;
            float cy = (y1 + y2) / 2.0f + sinrx * cxp + cosrx * cyp;

            // 4) Calculate theta1, and delta theta.
            float ux = (x1p - cxp) / rx;
            float uy = (y1p - cyp) / ry;
            float vx = (-x1p - cxp) / rx;
            float vy = (-y1p - cyp) / ry;
            float a1 = VecAng(1.0f, 0.0f, ux, uy);
            float da = VecAng(ux, uy, vx, vy);

            if (fs == 0 && da > 0)
            {
                da -= 2 * Pi;
            }
            else if (fs == 1 && da < 0)
            {
                da += 2 * Pi;
            }

            // Approximate the arc using cubic spline segments.
            float[] t = [cosrx, sinrx, -sinrx, cosrx, cx, cy];

            // Split arc into max 90 degree segments.
            int ndivs = (int)(MathF.Abs(da) / (Pi * 0.5f) + 1.0f);
            float hda = (da / ndivs) / 2.0f;
            // Fix for ticket #179: division by 0: avoid cotangens around 0 (infinite).
            if (hda is > -1e-3f and < 1e-3f)
            {
                hda *= 0.5f;
            }
            else
            {
                hda = (1.0f - MathF.Cos(hda)) / MathF.Sin(hda);
            }
            float kappa = MathF.Abs(4.0f / 3.0f * hda);
            if (da < 0.0f)
            {
                kappa = -kappa;
            }

            float px = 0, py = 0, ptanx = 0, ptany = 0;
            for (int i = 0; i <= ndivs; i++)
            {
                float a = a1 + da * (i / (float)ndivs);
                float adx = MathF.Cos(a);
                float ady = MathF.Sin(a);
                XformPoint(out float x, out float y, adx * rx, ady * ry, t);
                XformVec(out float tanx, out float tany, -ady * rx * kappa, adx * ry * kappa, t);
                if (i > 0)
                {
                    CubicBezTo(px + ptanx, py + ptany, x - tanx, y - tany, x, y);
                }
                px = x;
                py = y;
                ptanx = tanx;
                ptany = tany;
            }

            cpx = x2;
            cpy = y2;
        }

        private void ParsePath(XElement el)
        {
            string? d = null;
            foreach (XAttribute a in el.Attributes())
            {
                if (a.Name.LocalName == "d")
                {
                    d = a.Value;
                }
                else if (a.Name.LocalName == "style")
                {
                    ParseStyle(a.Value);
                }
                else
                {
                    ParseAttr(a.Name.LocalName, a.Value);
                }
            }

            if (d == null)
            {
                return;
            }

            ReadOnlySpan<char> s = d.AsSpan();
            Span<char> item = stackalloc char[64];
            char cmd = '\0';
            float[] args = new float[10];
            int nargs = 0;
            int rargs = 0;
            bool initPoint = false;
            bool closedFlag = false;
            float cpx = 0, cpy = 0, cpx2 = 0, cpy2 = 0;
            int pos = 0;

            ResetPath();
            while (pos < s.Length)
            {
                item[0] = '\0';
                if ((cmd == 'A' || cmd == 'a') && (nargs == 3 || nargs == 4))
                {
                    pos = GetNextPathItemWhenArcFlag(s, pos, item);
                }
                if (TokenLength(item) == 0)
                {
                    pos = GetNextPathItem(s, pos, item);
                }
                int itemLen = TokenLength(item);
                if (itemLen == 0) break;

                if (cmd != '\0' && itemLen > 0 && (IsDigit(item[0]) || item[0] == '-' || item[0] == '+' || item[0] == '.'))
                {
                    if (nargs < 10)
                    {
                        args[nargs++] = Atof(item[..itemLen]);
                    }
                    if (nargs >= rargs)
                    {
                        switch (cmd)
                        {
                            case 'm':
                            case 'M':
                                PathMoveTo(ref cpx, ref cpy, args, cmd == 'm');
                                // Moveto can be followed by multiple coordinate pairs,
                                // which should be treated as linetos.
                                cmd = cmd == 'm' ? 'l' : 'L';
                                rargs = GetArgsPerElement(cmd);
                                cpx2 = cpx; cpy2 = cpy;
                                initPoint = true;
                                break;
                            case 'l':
                            case 'L':
                                PathLineTo(ref cpx, ref cpy, args, cmd == 'l');
                                cpx2 = cpx; cpy2 = cpy;
                                break;
                            case 'H':
                            case 'h':
                                PathHLineTo(ref cpx, ref cpy, args, cmd == 'h');
                                cpx2 = cpx; cpy2 = cpy;
                                break;
                            case 'V':
                            case 'v':
                                PathVLineTo(ref cpx, ref cpy, args, cmd == 'v');
                                cpx2 = cpx; cpy2 = cpy;
                                break;
                            case 'C':
                            case 'c':
                                PathCubicBezTo(ref cpx, ref cpy, ref cpx2, ref cpy2, args, cmd == 'c');
                                break;
                            case 'S':
                            case 's':
                                PathCubicBezShortTo(ref cpx, ref cpy, ref cpx2, ref cpy2, args, cmd == 's');
                                break;
                            case 'Q':
                            case 'q':
                                PathQuadBezTo(ref cpx, ref cpy, ref cpx2, ref cpy2, args, cmd == 'q');
                                break;
                            case 'T':
                            case 't':
                                PathQuadBezShortTo(ref cpx, ref cpy, ref cpx2, ref cpy2, args, cmd == 't');
                                break;
                            case 'A':
                            case 'a':
                                PathArcTo(ref cpx, ref cpy, args, cmd == 'a');
                                cpx2 = cpx; cpy2 = cpy;
                                break;
                            default:
                                if (nargs >= 2)
                                {
                                    cpx = args[nargs - 2];
                                    cpy = args[nargs - 1];
                                    cpx2 = cpx; cpy2 = cpy;
                                }
                                break;
                        }
                        nargs = 0;
                    }
                }
                else
                {
                    cmd = item[0];
                    if (cmd == 'M' || cmd == 'm')
                    {
                        // Commit path.
                        if (_pts.Count > 0)
                        {
                            AddPath(closedFlag);
                        }
                        // Start new subpath.
                        ResetPath();
                        closedFlag = false;
                        nargs = 0;
                    }
                    else if (!initPoint)
                    {
                        // Do not allow other commands until an initial point has been set.
                        cmd = '\0';
                    }
                    if (cmd == 'Z' || cmd == 'z')
                    {
                        closedFlag = true;
                        // Commit path.
                        if (_pts.Count > 0)
                        {
                            // Move current point to first point.
                            cpx = _pts[0];
                            cpy = _pts[1];
                            cpx2 = cpx; cpy2 = cpy;
                            AddPath(closedFlag);
                        }
                        // Start new subpath.
                        ResetPath();
                        MoveTo(cpx, cpy);
                        closedFlag = false;
                        nargs = 0;
                    }
                    rargs = GetArgsPerElement(cmd);
                    if (rargs == -1)
                    {
                        // Command not recognized.
                        cmd = '\0';
                        rargs = 0;
                    }
                }
            }
            // Commit path.
            if (_pts.Count > 0)
            {
                AddPath(closedFlag);
            }

            AddShape();
        }

        // ------------------------------------------------------------------
        // Basic shapes.
        // ------------------------------------------------------------------

        private void ParseRect(XElement el)
        {
            float x = 0.0f, y = 0.0f, w = 0.0f, h = 0.0f;
            float rx = -1.0f, ry = -1.0f; // -1 marks not set

            foreach (XAttribute a in el.Attributes())
            {
                string name = a.Name.LocalName;
                if (!ParseAttr(name, a.Value))
                {
                    switch (name)
                    {
                        case "x": x = ParseCoordinate(a.Value, _viewMinX, _viewWidth); break;
                        case "y": y = ParseCoordinate(a.Value, _viewMinY, _viewHeight); break;
                        case "width": w = ParseCoordinate(a.Value, 0.0f, _viewWidth); break;
                        case "height": h = ParseCoordinate(a.Value, 0.0f, _viewHeight); break;
                        case "rx": rx = MathF.Abs(ParseCoordinate(a.Value, 0.0f, _viewWidth)); break;
                        case "ry": ry = MathF.Abs(ParseCoordinate(a.Value, 0.0f, _viewHeight)); break;
                    }
                }
            }

            if (rx < 0.0f && ry > 0.0f) rx = ry;
            if (ry < 0.0f && rx > 0.0f) ry = rx;
            if (rx < 0.0f) rx = 0.0f;
            if (ry < 0.0f) ry = 0.0f;
            if (rx > w / 2.0f) rx = w / 2.0f;
            if (ry > h / 2.0f) ry = h / 2.0f;

            if (w != 0.0f && h != 0.0f)
            {
                ResetPath();

                if (rx < 0.00001f || ry < 0.0001f)
                {
                    MoveTo(x, y);
                    LineTo(x + w, y);
                    LineTo(x + w, y + h);
                    LineTo(x, y + h);
                }
                else
                {
                    // Rounded rectangle.
                    MoveTo(x + rx, y);
                    LineTo(x + w - rx, y);
                    CubicBezTo(x + w - rx * (1 - Kappa90), y, x + w, y + ry * (1 - Kappa90), x + w, y + ry);
                    LineTo(x + w, y + h - ry);
                    CubicBezTo(x + w, y + h - ry * (1 - Kappa90), x + w - rx * (1 - Kappa90), y + h, x + w - rx, y + h);
                    LineTo(x + rx, y + h);
                    CubicBezTo(x + rx * (1 - Kappa90), y + h, x, y + h - ry * (1 - Kappa90), x, y + h - ry);
                    LineTo(x, y + ry);
                    CubicBezTo(x, y + ry * (1 - Kappa90), x + rx * (1 - Kappa90), y, x + rx, y);
                }

                AddPath(true);

                AddShape();
            }
        }

        private void ParseCircle(XElement el)
        {
            float cx = 0.0f, cy = 0.0f, r = 0.0f;

            foreach (XAttribute a in el.Attributes())
            {
                string name = a.Name.LocalName;
                if (!ParseAttr(name, a.Value))
                {
                    switch (name)
                    {
                        case "cx": cx = ParseCoordinate(a.Value, _viewMinX, _viewWidth); break;
                        case "cy": cy = ParseCoordinate(a.Value, _viewMinY, _viewHeight); break;
                        case "r": r = MathF.Abs(ParseCoordinate(a.Value, 0.0f, ActualLength())); break;
                    }
                }
            }

            if (r > 0.0f)
            {
                ResetPath();

                MoveTo(cx + r, cy);
                CubicBezTo(cx + r, cy + r * Kappa90, cx + r * Kappa90, cy + r, cx, cy + r);
                CubicBezTo(cx - r * Kappa90, cy + r, cx - r, cy + r * Kappa90, cx - r, cy);
                CubicBezTo(cx - r, cy - r * Kappa90, cx - r * Kappa90, cy - r, cx, cy - r);
                CubicBezTo(cx + r * Kappa90, cy - r, cx + r, cy - r * Kappa90, cx + r, cy);

                AddPath(true);

                AddShape();
            }
        }

        private void ParseEllipse(XElement el)
        {
            float cx = 0.0f, cy = 0.0f, rx = 0.0f, ry = 0.0f;

            foreach (XAttribute a in el.Attributes())
            {
                string name = a.Name.LocalName;
                if (!ParseAttr(name, a.Value))
                {
                    switch (name)
                    {
                        case "cx": cx = ParseCoordinate(a.Value, _viewMinX, _viewWidth); break;
                        case "cy": cy = ParseCoordinate(a.Value, _viewMinY, _viewHeight); break;
                        case "rx": rx = MathF.Abs(ParseCoordinate(a.Value, 0.0f, _viewWidth)); break;
                        case "ry": ry = MathF.Abs(ParseCoordinate(a.Value, 0.0f, _viewHeight)); break;
                    }
                }
            }

            if (rx > 0.0f && ry > 0.0f)
            {
                ResetPath();

                MoveTo(cx + rx, cy);
                CubicBezTo(cx + rx, cy + ry * Kappa90, cx + rx * Kappa90, cy + ry, cx, cy + ry);
                CubicBezTo(cx - rx * Kappa90, cy + ry, cx - rx, cy + ry * Kappa90, cx - rx, cy);
                CubicBezTo(cx - rx, cy - ry * Kappa90, cx - rx * Kappa90, cy - ry, cx, cy - ry);
                CubicBezTo(cx + rx * Kappa90, cy - ry, cx + rx, cy - ry * Kappa90, cx + rx, cy);

                AddPath(true);

                AddShape();
            }
        }

        private void ParseLine(XElement el)
        {
            float x1 = 0.0f, y1 = 0.0f, x2 = 0.0f, y2 = 0.0f;

            foreach (XAttribute a in el.Attributes())
            {
                string name = a.Name.LocalName;
                if (!ParseAttr(name, a.Value))
                {
                    switch (name)
                    {
                        case "x1": x1 = ParseCoordinate(a.Value, _viewMinX, _viewWidth); break;
                        case "y1": y1 = ParseCoordinate(a.Value, _viewMinY, _viewHeight); break;
                        case "x2": x2 = ParseCoordinate(a.Value, _viewMinX, _viewWidth); break;
                        case "y2": y2 = ParseCoordinate(a.Value, _viewMinY, _viewHeight); break;
                    }
                }
            }

            ResetPath();

            MoveTo(x1, y1);
            LineTo(x2, y2);

            AddPath(false);

            AddShape();
        }

        private void ParsePoly(XElement el, bool closeFlag)
        {
            ResetPath();

            Span<char> item = stackalloc char[64];
            foreach (XAttribute a in el.Attributes())
            {
                if (!ParseAttr(a.Name.LocalName, a.Value) && a.Name.LocalName == "points")
                {
                    ReadOnlySpan<char> s = a.Value.AsSpan();
                    int pos = 0;
                    float a0 = 0, a1 = 0;
                    int nargs = 0;
                    int npts = 0;
                    while (pos < s.Length)
                    {
                        pos = GetNextPathItem(s, pos, item);
                        int len = TokenLength(item);
                        if (len == 0) break;
                        float v = Atof(item[..len]);
                        if (nargs == 0) a0 = v;
                        else a1 = v;
                        nargs++;
                        if (nargs >= 2)
                        {
                            if (npts == 0)
                            {
                                MoveTo(a0, a1);
                            }
                            else
                            {
                                LineTo(a0, a1);
                            }
                            nargs = 0;
                            npts++;
                        }
                    }
                }
            }

            AddPath(closeFlag);

            AddShape();
        }

        private void ParseSvg(XElement el)
        {
            foreach (XAttribute a in el.Attributes())
            {
                string name = a.Name.LocalName;
                if (!ParseAttr(name, a.Value))
                {
                    switch (name)
                    {
                        case "width":
                            _image.Width = ParseCoordinate(a.Value, 0.0f, 0.0f);
                            break;
                        case "height":
                            _image.Height = ParseCoordinate(a.Value, 0.0f, 0.0f);
                            break;
                        case "viewBox":
                        {
                            ReadOnlySpan<char> s = a.Value.AsSpan();
                            Span<char> buf = stackalloc char[64];
                            int pos = 0;
                            pos = ParseNumber(s, pos, buf);
                            _viewMinX = Atof(buf[..TokenLength(buf)]);
                            SkipViewBoxSeparator(s, ref pos);
                            if (pos >= s.Length) return;
                            pos = ParseNumber(s, pos, buf);
                            _viewMinY = Atof(buf[..TokenLength(buf)]);
                            SkipViewBoxSeparator(s, ref pos);
                            if (pos >= s.Length) return;
                            pos = ParseNumber(s, pos, buf);
                            _viewWidth = Atof(buf[..TokenLength(buf)]);
                            SkipViewBoxSeparator(s, ref pos);
                            if (pos >= s.Length) return;
                            pos = ParseNumber(s, pos, buf);
                            _viewHeight = Atof(buf[..TokenLength(buf)]);
                            break;
                        }
                        case "preserveAspectRatio":
                        {
                            string v = a.Value;
                            if (v.Contains("none", StringComparison.Ordinal))
                            {
                                // No uniform scaling.
                                _alignType = AlignNone;
                            }
                            else
                            {
                                // Parse X align.
                                if (v.Contains("xMin", StringComparison.Ordinal)) _alignX = AlignMin;
                                else if (v.Contains("xMid", StringComparison.Ordinal)) _alignX = AlignMid;
                                else if (v.Contains("xMax", StringComparison.Ordinal)) _alignX = AlignMax;
                                // Parse Y align.
                                if (v.Contains("yMin", StringComparison.Ordinal)) _alignY = AlignMin;
                                else if (v.Contains("yMid", StringComparison.Ordinal)) _alignY = AlignMid;
                                else if (v.Contains("yMax", StringComparison.Ordinal)) _alignY = AlignMax;
                                // Parse meet/slice.
                                _alignType = v.Contains("slice", StringComparison.Ordinal) ? AlignSlice : AlignMeet;
                            }
                            break;
                        }
                    }
                }
            }
        }

        private static void SkipViewBoxSeparator(ReadOnlySpan<char> s, ref int pos)
        {
            while (pos < s.Length && (IsSpace(s[pos]) || s[pos] == '%' || s[pos] == ',')) pos++;
        }

        // ------------------------------------------------------------------
        // Gradients.
        // ------------------------------------------------------------------

        private GradientData? FindGradientData(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            // The list is head-inserted, so later definitions win, like the C code.
            for (int i = 0; i < _gradients.Count; i++)
            {
                if (_gradients[i].Id == id)
                {
                    return _gradients[i];
                }
            }
            return null;
        }

        private void ParseGradient(XElement el, NanoSvgPaintType type)
        {
            GradientData grad = new GradientData
            {
                Type = type,
            };
            if (type == NanoSvgPaintType.LinearGradient)
            {
                grad.Lx1 = new Coordinate(0.0f, Units.Percent);
                grad.Ly1 = new Coordinate(0.0f, Units.Percent);
                grad.Lx2 = new Coordinate(100.0f, Units.Percent);
                grad.Ly2 = new Coordinate(0.0f, Units.Percent);
            }
            else
            {
                grad.Cx = new Coordinate(50.0f, Units.Percent);
                grad.Cy = new Coordinate(50.0f, Units.Percent);
                grad.R = new Coordinate(50.0f, Units.Percent);
            }

            XformIdentity(grad.Xform);

            foreach (XAttribute a in el.Attributes())
            {
                string name = a.Name.LocalName;
                if (name == "id")
                {
                    grad.Id = a.Value;
                }
                else if (name == "gradientUnits")
                {
                    grad.ObjectSpaceUnits = a.Value == "objectBoundingBox";
                }
                else if (name == "gradientTransform")
                {
                    ParseTransform(grad.Xform, a.Value);
                }
                else if (name == "cx") grad.Cx = ParseCoordinateRaw(a.Value);
                else if (name == "cy") grad.Cy = ParseCoordinateRaw(a.Value);
                else if (name == "r") grad.R = ParseCoordinateRaw(a.Value);
                else if (name == "fx") grad.Fx = ParseCoordinateRaw(a.Value);
                else if (name == "fy") grad.Fy = ParseCoordinateRaw(a.Value);
                else if (name == "x1") grad.Lx1 = ParseCoordinateRaw(a.Value);
                else if (name == "y1") grad.Ly1 = ParseCoordinateRaw(a.Value);
                else if (name == "x2") grad.Lx2 = ParseCoordinateRaw(a.Value);
                else if (name == "y2") grad.Ly2 = ParseCoordinateRaw(a.Value);
                else if (name == "spreadMethod")
                {
                    grad.Spread = a.Value switch
                    {
                        "pad" => NanoSvgSpreadType.Pad,
                        "reflect" => NanoSvgSpreadType.Reflect,
                        "repeat" => NanoSvgSpreadType.Repeat,
                        _ => grad.Spread,
                    };
                }
                else if (name == "href")
                {
                    // xlink:href resolves to the same local name.
                    string href = a.Value;
                    grad.Ref = href.StartsWith('#') ? href[1..] : href;
                }
            }

            _gradients.Insert(0, grad);
        }

        private void ParseGradientStop(XElement el)
        {
            AttribState curAttr = GetAttr();
            curAttr.StopOffset = 0;
            curAttr.StopColor = 0;
            curAttr.StopOpacity = 1.0f;

            foreach (XAttribute a in el.Attributes())
            {
                ParseAttr(a.Name.LocalName, a.Value);
            }

            // Add stop to the last gradient.
            if (_gradients.Count == 0)
            {
                return;
            }
            GradientData grad = _gradients[0];

            uint color = curAttr.StopColor | ((uint)(curAttr.StopOpacity * 255) << 24);
            NanoSvgGradientStop stop = new NanoSvgGradientStop(color, curAttr.StopOffset);

            // Insert, keeping stops sorted by offset.
            int idx = grad.Stops.Count;
            for (int i = 0; i < grad.Stops.Count; i++)
            {
                if (stop.Offset < grad.Stops[i].Offset)
                {
                    idx = i;
                    break;
                }
            }
            grad.Stops.Insert(idx, stop);
        }

        private void GetLocalBounds(float[] bounds, NanoSvgShape shape, float[] xform)
        {
            float[] curve = new float[8];
            float[] curveBounds = new float[4];
            bool first = true;
            foreach (NanoSvgPath path in shape.Paths)
            {
                XformPoint(out curve[0], out curve[1], path.Pts[0], path.Pts[1], xform);
                for (int i = 0; i < path.Pts.Length / 2 - 1; i += 3)
                {
                    XformPoint(out curve[2], out curve[3], path.Pts[(i + 1) * 2], path.Pts[(i + 1) * 2 + 1], xform);
                    XformPoint(out curve[4], out curve[5], path.Pts[(i + 2) * 2], path.Pts[(i + 2) * 2 + 1], xform);
                    XformPoint(out curve[6], out curve[7], path.Pts[(i + 3) * 2], path.Pts[(i + 3) * 2 + 1], xform);
                    CurveBounds(curveBounds, curve);
                    if (first)
                    {
                        Array.Copy(curveBounds, bounds, 4);
                        first = false;
                    }
                    else
                    {
                        bounds[0] = MathF.Min(bounds[0], curveBounds[0]);
                        bounds[1] = MathF.Min(bounds[1], curveBounds[1]);
                        bounds[2] = MathF.Max(bounds[2], curveBounds[2]);
                        bounds[3] = MathF.Max(bounds[3], curveBounds[3]);
                    }
                    curve[0] = curve[6];
                    curve[1] = curve[7];
                }
            }
        }

        private NanoSvgGradient? CreateGradient(string id, float[] localBounds, float[] xform, out NanoSvgPaintType paintType)
        {
            paintType = NanoSvgPaintType.None;
            GradientData? data = FindGradientData(id);
            if (data == null)
            {
                return null;
            }

            // Follow gradient references (from other gradients) to find the stops.
            List<NanoSvgGradientStop>? stops = null;
            GradientData? reference = data;
            int refIter = 0;
            while (reference != null)
            {
                if (stops == null && reference.Stops.Count > 0)
                {
                    stops = reference.Stops;
                    break;
                }
                GradientData? nextRef = FindGradientData(reference.Ref);
                if (nextRef == reference) break; // prevent infinite loops on malformed data
                reference = nextRef;
                refIter++;
                if (refIter > 32) break; // prevent infinite loops on malformed data
            }
            if (stops == null)
            {
                return null;
            }

            NanoSvgGradient grad = new NanoSvgGradient();

            // The shape width and height.
            float ox, oy, sw, sh;
            if (data.ObjectSpaceUnits)
            {
                ox = localBounds[0];
                oy = localBounds[1];
                sw = localBounds[2] - localBounds[0];
                sh = localBounds[3] - localBounds[1];
            }
            else
            {
                ox = _viewMinX;
                oy = _viewMinY;
                sw = _viewWidth;
                sh = _viewHeight;
            }
            float sl = MathF.Sqrt(sw * sw + sh * sh) / MathF.Sqrt(2.0f);

            if (data.Type == NanoSvgPaintType.LinearGradient)
            {
                float x1 = ConvertToPixels(data.Lx1, ox, sw);
                float y1 = ConvertToPixels(data.Ly1, oy, sh);
                float x2 = ConvertToPixels(data.Lx2, ox, sw);
                float y2 = ConvertToPixels(data.Ly2, oy, sh);
                // Calculate transform aligned to the line (t = gradient-space y).
                float dx = x2 - x1;
                float dy = y2 - y1;
                grad.Xform[0] = dy; grad.Xform[1] = -dx;
                grad.Xform[2] = dx; grad.Xform[3] = dy;
                grad.Xform[4] = x1; grad.Xform[5] = y1;
            }
            else
            {
                float cx = ConvertToPixels(data.Cx, ox, sw);
                float cy = ConvertToPixels(data.Cy, oy, sh);
                float fx = ConvertToPixels(data.Fx, ox, sw);
                float fy = ConvertToPixels(data.Fy, oy, sh);
                float r = ConvertToPixels(data.R, 0, sl);
                if (r <= 1e-6f)
                {
                    return null;
                }
                // Calculate transform aligned to the circle (unit circle at origin).
                grad.Xform[0] = r; grad.Xform[1] = 0;
                grad.Xform[2] = 0; grad.Xform[3] = r;
                grad.Xform[4] = cx; grad.Xform[5] = cy;
                grad.Fx = fx / r;
                grad.Fy = fy / r;
            }

            XformMultiply(grad.Xform, data.Xform);
            XformMultiply(grad.Xform, xform);

            grad.Spread = data.Spread;
            grad.Stops.AddRange(stops);

            paintType = data.Type;

            return grad;
        }

        /// <summary>Resolves pending url() gradient paints on all shapes.</summary>
        public void CreateGradients()
        {
            foreach (NanoSvgShape shape in _image.Shapes)
            {
                if (shape.Fill.Type == NanoSvgPaintType.Undefined)
                {
                    NanoSvgPaint fill = shape.Fill;
                    if (shape.FillGradientId.Length != 0)
                    {
                        float[] inv = new float[6];
                        float[] localBounds = new float[4];
                        XformInverse(inv, shape.Xform);
                        GetLocalBounds(localBounds, shape, inv);
                        fill.Gradient = CreateGradient(shape.FillGradientId, localBounds, shape.Xform, out NanoSvgPaintType type);
                        fill.Type = type;
                    }
                    if (fill.Type == NanoSvgPaintType.Undefined)
                    {
                        fill.Type = NanoSvgPaintType.None;
                    }
                    shape.Fill = fill;
                }
                if (shape.Stroke.Type == NanoSvgPaintType.Undefined)
                {
                    NanoSvgPaint stroke = shape.Stroke;
                    if (shape.StrokeGradientId.Length != 0)
                    {
                        float[] inv = new float[6];
                        float[] localBounds = new float[4];
                        XformInverse(inv, shape.Xform);
                        GetLocalBounds(localBounds, shape, inv);
                        stroke.Gradient = CreateGradient(shape.StrokeGradientId, localBounds, shape.Xform, out NanoSvgPaintType type);
                        stroke.Type = type;
                    }
                    if (stroke.Type == NanoSvgPaintType.Undefined)
                    {
                        stroke.Type = NanoSvgPaintType.None;
                    }
                    shape.Stroke = stroke;
                }
            }
        }

        // ------------------------------------------------------------------
        // Viewbox scaling.
        // ------------------------------------------------------------------

        private void ImageBounds(float[] bounds)
        {
            if (_image.Shapes.Count == 0)
            {
                bounds[0] = bounds[1] = bounds[2] = bounds[3] = 0.0f;
                return;
            }
            Array.Copy(_image.Shapes[0].Bounds, bounds, 4);
            for (int i = 1; i < _image.Shapes.Count; i++)
            {
                float[] b = _image.Shapes[i].Bounds;
                bounds[0] = MathF.Min(bounds[0], b[0]);
                bounds[1] = MathF.Min(bounds[1], b[1]);
                bounds[2] = MathF.Max(bounds[2], b[2]);
                bounds[3] = MathF.Max(bounds[3], b[3]);
            }
        }

        private static float ViewAlign(float content, float container, int type)
        {
            if (type == AlignMin)
            {
                return 0;
            }
            if (type == AlignMax)
            {
                return container - content;
            }
            // mid
            return (container - content) * 0.5f;
        }

        private static void ScaleGradient(NanoSvgGradient grad, float tx, float ty, float sx, float sy)
        {
            float[] t = new float[6];
            XformSetTranslation(t, tx, ty);
            XformMultiply(grad.Xform, t);

            XformSetScale(t, sx, sy);
            XformMultiply(grad.Xform, t);
        }

        public void ScaleToViewbox(string units)
        {
            // Guess image size if not set completely.
            float[] bounds = new float[4];
            ImageBounds(bounds);

            if (_viewWidth == 0)
            {
                if (_image.Width > 0)
                {
                    _viewWidth = _image.Width;
                }
                else
                {
                    _viewMinX = bounds[0];
                    _viewWidth = bounds[2] - bounds[0];
                }
            }
            if (_viewHeight == 0)
            {
                if (_image.Height > 0)
                {
                    _viewHeight = _image.Height;
                }
                else
                {
                    _viewMinY = bounds[1];
                    _viewHeight = bounds[3] - bounds[1];
                }
            }
            if (_image.Width == 0)
            {
                _image.Width = _viewWidth;
            }
            if (_image.Height == 0)
            {
                _image.Height = _viewHeight;
            }

            float tx = -_viewMinX;
            float ty = -_viewMinY;
            float sx = _viewWidth > 0 ? _image.Width / _viewWidth : 0;
            float sy = _viewHeight > 0 ? _image.Height / _viewHeight : 0;
            // Unit scaling.
            float us = 1.0f / ConvertToPixels(new Coordinate(1.0f, ParseUnits(units.AsSpan())), 0.0f, 1.0f);

            // Fix aspect ratio.
            if (_alignType == AlignMeet)
            {
                // Fit whole image into viewbox.
                sx = sy = MathF.Min(sx, sy);
                tx += ViewAlign(_viewWidth * sx, _image.Width, _alignX) / sx;
                ty += ViewAlign(_viewHeight * sy, _image.Height, _alignY) / sy;
            }
            else if (_alignType == AlignSlice)
            {
                // Fill whole viewbox with image.
                sx = sy = MathF.Max(sx, sy);
                tx += ViewAlign(_viewWidth * sx, _image.Width, _alignX) / sx;
                ty += ViewAlign(_viewHeight * sy, _image.Height, _alignY) / sy;
            }

            // Transform.
            sx *= us;
            sy *= us;
            float avgs = (sx + sy) / 2.0f;
            foreach (NanoSvgShape shape in _image.Shapes)
            {
                shape.Bounds[0] = (shape.Bounds[0] + tx) * sx;
                shape.Bounds[1] = (shape.Bounds[1] + ty) * sy;
                shape.Bounds[2] = (shape.Bounds[2] + tx) * sx;
                shape.Bounds[3] = (shape.Bounds[3] + ty) * sy;
                foreach (NanoSvgPath path in shape.Paths)
                {
                    path.Bounds[0] = (path.Bounds[0] + tx) * sx;
                    path.Bounds[1] = (path.Bounds[1] + ty) * sy;
                    path.Bounds[2] = (path.Bounds[2] + tx) * sx;
                    path.Bounds[3] = (path.Bounds[3] + ty) * sy;
                    for (int i = 0; i < path.Pts.Length; i += 2)
                    {
                        path.Pts[i] = (path.Pts[i] + tx) * sx;
                        path.Pts[i + 1] = (path.Pts[i + 1] + ty) * sy;
                    }
                }

                if (shape.Fill.Gradient != null)
                {
                    ScaleGradient(shape.Fill.Gradient, tx, ty, sx, sy);
                    float[] t = (float[])shape.Fill.Gradient.Xform.Clone();
                    XformInverse(shape.Fill.Gradient.Xform, t);
                }
                if (shape.Stroke.Gradient != null)
                {
                    ScaleGradient(shape.Stroke.Gradient, tx, ty, sx, sy);
                    float[] t = (float[])shape.Stroke.Gradient.Xform.Clone();
                    XformInverse(shape.Stroke.Gradient.Xform, t);
                }

                shape.StrokeWidth *= avgs;
                shape.StrokeDashOffset *= avgs;
                for (int i = 0; i < shape.StrokeDashArray.Length; i++)
                {
                    shape.StrokeDashArray[i] *= avgs;
                }
            }
        }
    }

    private static readonly Dictionary<string, uint> NamedColors = new()
    {
        ["red"] = Rgb(255, 0, 0),
        ["green"] = Rgb(0, 128, 0),
        ["blue"] = Rgb(0, 0, 255),
        ["yellow"] = Rgb(255, 255, 0),
        ["cyan"] = Rgb(0, 255, 255),
        ["magenta"] = Rgb(255, 0, 255),
        ["black"] = Rgb(0, 0, 0),
        ["grey"] = Rgb(128, 128, 128),
        ["gray"] = Rgb(128, 128, 128),
        ["white"] = Rgb(255, 255, 255),
        ["aliceblue"] = Rgb(240, 248, 255),
        ["antiquewhite"] = Rgb(250, 235, 215),
        ["aqua"] = Rgb(0, 255, 255),
        ["aquamarine"] = Rgb(127, 255, 212),
        ["azure"] = Rgb(240, 255, 255),
        ["beige"] = Rgb(245, 245, 220),
        ["bisque"] = Rgb(255, 228, 196),
        ["blanchedalmond"] = Rgb(255, 235, 205),
        ["blueviolet"] = Rgb(138, 43, 226),
        ["brown"] = Rgb(165, 42, 42),
        ["burlywood"] = Rgb(222, 184, 135),
        ["cadetblue"] = Rgb(95, 158, 160),
        ["chartreuse"] = Rgb(127, 255, 0),
        ["chocolate"] = Rgb(210, 105, 30),
        ["coral"] = Rgb(255, 127, 80),
        ["cornflowerblue"] = Rgb(100, 149, 237),
        ["cornsilk"] = Rgb(255, 248, 220),
        ["crimson"] = Rgb(220, 20, 60),
        ["darkblue"] = Rgb(0, 0, 139),
        ["darkcyan"] = Rgb(0, 139, 139),
        ["darkgoldenrod"] = Rgb(184, 134, 11),
        ["darkgray"] = Rgb(169, 169, 169),
        ["darkgreen"] = Rgb(0, 100, 0),
        ["darkgrey"] = Rgb(169, 169, 169),
        ["darkkhaki"] = Rgb(189, 183, 107),
        ["darkmagenta"] = Rgb(139, 0, 139),
        ["darkolivegreen"] = Rgb(85, 107, 47),
        ["darkorange"] = Rgb(255, 140, 0),
        ["darkorchid"] = Rgb(153, 50, 204),
        ["darkred"] = Rgb(139, 0, 0),
        ["darksalmon"] = Rgb(233, 150, 122),
        ["darkseagreen"] = Rgb(143, 188, 143),
        ["darkslateblue"] = Rgb(72, 61, 139),
        ["darkslategray"] = Rgb(47, 79, 79),
        ["darkslategrey"] = Rgb(47, 79, 79),
        ["darkturquoise"] = Rgb(0, 206, 209),
        ["darkviolet"] = Rgb(148, 0, 211),
        ["deeppink"] = Rgb(255, 20, 147),
        ["deepskyblue"] = Rgb(0, 191, 255),
        ["dimgray"] = Rgb(105, 105, 105),
        ["dimgrey"] = Rgb(105, 105, 105),
        ["dodgerblue"] = Rgb(30, 144, 255),
        ["firebrick"] = Rgb(178, 34, 34),
        ["floralwhite"] = Rgb(255, 250, 240),
        ["forestgreen"] = Rgb(34, 139, 34),
        ["fuchsia"] = Rgb(255, 0, 255),
        ["gainsboro"] = Rgb(220, 220, 220),
        ["ghostwhite"] = Rgb(248, 248, 255),
        ["gold"] = Rgb(255, 215, 0),
        ["goldenrod"] = Rgb(218, 165, 32),
        ["greenyellow"] = Rgb(173, 255, 47),
        ["honeydew"] = Rgb(240, 255, 240),
        ["hotpink"] = Rgb(255, 105, 180),
        ["indianred"] = Rgb(205, 92, 92),
        ["indigo"] = Rgb(75, 0, 130),
        ["ivory"] = Rgb(255, 255, 240),
        ["khaki"] = Rgb(240, 230, 140),
        ["lavender"] = Rgb(230, 230, 250),
        ["lavenderblush"] = Rgb(255, 240, 245),
        ["lawngreen"] = Rgb(124, 252, 0),
        ["lemonchiffon"] = Rgb(255, 250, 205),
        ["lightblue"] = Rgb(173, 216, 230),
        ["lightcoral"] = Rgb(240, 128, 128),
        ["lightcyan"] = Rgb(224, 255, 255),
        ["lightgoldenrodyellow"] = Rgb(250, 250, 210),
        ["lightgray"] = Rgb(211, 211, 211),
        ["lightgreen"] = Rgb(144, 238, 144),
        ["lightgrey"] = Rgb(211, 211, 211),
        ["lightpink"] = Rgb(255, 182, 193),
        ["lightsalmon"] = Rgb(255, 160, 122),
        ["lightseagreen"] = Rgb(32, 178, 170),
        ["lightskyblue"] = Rgb(135, 206, 250),
        ["lightslategray"] = Rgb(119, 136, 153),
        ["lightslategrey"] = Rgb(119, 136, 153),
        ["lightsteelblue"] = Rgb(176, 196, 222),
        ["lightyellow"] = Rgb(255, 255, 224),
        ["lime"] = Rgb(0, 255, 0),
        ["limegreen"] = Rgb(50, 205, 50),
        ["linen"] = Rgb(250, 240, 230),
        ["maroon"] = Rgb(128, 0, 0),
        ["mediumaquamarine"] = Rgb(102, 205, 170),
        ["mediumblue"] = Rgb(0, 0, 205),
        ["mediumorchid"] = Rgb(186, 85, 211),
        ["mediumpurple"] = Rgb(147, 112, 219),
        ["mediumseagreen"] = Rgb(60, 179, 113),
        ["mediumslateblue"] = Rgb(123, 104, 238),
        ["mediumspringgreen"] = Rgb(0, 250, 154),
        ["mediumturquoise"] = Rgb(72, 209, 204),
        ["mediumvioletred"] = Rgb(199, 21, 133),
        ["midnightblue"] = Rgb(25, 25, 112),
        ["mintcream"] = Rgb(245, 255, 250),
        ["mistyrose"] = Rgb(255, 228, 225),
        ["moccasin"] = Rgb(255, 228, 181),
        ["navajowhite"] = Rgb(255, 222, 173),
        ["navy"] = Rgb(0, 0, 128),
        ["oldlace"] = Rgb(253, 245, 230),
        ["olive"] = Rgb(128, 128, 0),
        ["olivedrab"] = Rgb(107, 142, 35),
        ["orange"] = Rgb(255, 165, 0),
        ["orangered"] = Rgb(255, 69, 0),
        ["orchid"] = Rgb(218, 112, 214),
        ["palegoldenrod"] = Rgb(238, 232, 170),
        ["palegreen"] = Rgb(152, 251, 152),
        ["paleturquoise"] = Rgb(175, 238, 238),
        ["palevioletred"] = Rgb(219, 112, 147),
        ["papayawhip"] = Rgb(255, 239, 213),
        ["peachpuff"] = Rgb(255, 218, 185),
        ["peru"] = Rgb(205, 133, 63),
        ["pink"] = Rgb(255, 192, 203),
        ["plum"] = Rgb(221, 160, 221),
        ["powderblue"] = Rgb(176, 224, 230),
        ["purple"] = Rgb(128, 0, 128),
        ["rosybrown"] = Rgb(188, 143, 143),
        ["royalblue"] = Rgb(65, 105, 225),
        ["saddlebrown"] = Rgb(139, 69, 19),
        ["salmon"] = Rgb(250, 128, 114),
        ["sandybrown"] = Rgb(244, 164, 96),
        ["seagreen"] = Rgb(46, 139, 87),
        ["seashell"] = Rgb(255, 245, 238),
        ["sienna"] = Rgb(160, 82, 45),
        ["silver"] = Rgb(192, 192, 192),
        ["skyblue"] = Rgb(135, 206, 235),
        ["slateblue"] = Rgb(106, 90, 205),
        ["slategray"] = Rgb(112, 128, 144),
        ["slategrey"] = Rgb(112, 128, 144),
        ["snow"] = Rgb(255, 250, 250),
        ["springgreen"] = Rgb(0, 255, 127),
        ["steelblue"] = Rgb(70, 130, 180),
        ["tan"] = Rgb(210, 180, 140),
        ["teal"] = Rgb(0, 128, 128),
        ["thistle"] = Rgb(216, 191, 216),
        ["tomato"] = Rgb(255, 99, 71),
        ["turquoise"] = Rgb(64, 224, 208),
        ["violet"] = Rgb(238, 130, 238),
        ["wheat"] = Rgb(245, 222, 179),
        ["whitesmoke"] = Rgb(245, 245, 245),
        ["yellowgreen"] = Rgb(154, 205, 50),
    };
}
