using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

/// <summary>
/// The input box UI node.
/// </summary>
public class UIInputBox : UISelectable
{
    public const int MinSpanFormattableSize = 32;
    private struct Line
    {
        public int start;
        public int count;
        public float width;
    }
    private readonly ArrayBuffer<char> _text = new ArrayBuffer<char>(); // for less GC
    private readonly List<Line> _lines = new List<Line>();
    private int _textLength;
    private float _fontSize = 16f;
    private string _tmpStr = string.Empty;
    private bool _isTmpStrReadDirty;
    private bool _isTmpStrWriteDirty;
    private bool _canInputText;
    private Pivot _textPivot = Pivot.Center; // the pivot of the text relative to the container
    private OverflowModeHorizontal _overflowHorizontal = OverflowModeHorizontal.None;
    private OverflowModeVertical _overflowVertical = OverflowModeVertical.None;


    public Font? Font { get; set; }
    public float FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            if (_overflowHorizontal == OverflowModeHorizontal.NextLine)
            {
                RefreshTextLineBreak();
            }
        }
    }
    public float LineSpacing { get; set; } = 1f;
    public ColorFloat Color { get; set; } = 0xffffff;


    public string Text
    {
        get
        {
            if (_isTmpStrReadDirty)
            {
                _tmpStr = new string(_text.Span);
                _isTmpStrReadDirty = false;
            }
            return _tmpStr;
        }
        set
        {
            //SetText(value);
            _tmpStr = value;
            _isTmpStrWriteDirty = true;
        }
    }

    public Pivot TextPivot
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textPivot;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot = value;
    }

    public OverflowModeHorizontal OverflowHorizontal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _overflowHorizontal;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _overflowHorizontal = value;
    }

    public OverflowModeVertical OverflowVertical
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _overflowVertical;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _overflowVertical = value;
    }

    public TextAlign AlignVertical
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textPivot.Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot.Y = value;

    }

    public TextAlign AlignHorizontal
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textPivot.X;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _textPivot.X = value;
    }

    public UIInputBox()
    {

    }

    protected unsafe override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);
        if (Font == null)
        {
            return;
        }

        if (_isTmpStrWriteDirty)
        {
            SetText(_tmpStr);
        }

        if (_textLength == 0)
        {
            return;
        }

        CanvasRenderer renderer = canvas.Renderer;
        Transform2D transform = WorldTransform;
        transform.position += transform.scale * Size * TextPivot;
        transform.scale *= _fontSize;
        float lineHeight = _fontSize * LineSpacing;
        float offsetY = (_lines.Count - 1) * lineHeight * (0.5f - _textPivot.Y);
        transform.position.Y += offsetY;

        BoundingBox2D mask = Mask;
        if (!HasMask)
        {
            mask = canvas.Bound;
        }

        if (_overflowHorizontal == OverflowModeHorizontal.Clamp)
        {
            //mask = Bound;
            mask.min.X = math.max(mask.min.X, Bound.min.X);
            mask.max.X = math.min(mask.max.X, Bound.max.X);
        }

        if (_overflowVertical == OverflowModeVertical.Clamp)
        {
            //mask = Bound;
            mask.min.Y = math.max(mask.min.Y, Bound.min.Y);
            mask.max.Y = math.min(mask.max.Y, Bound.max.Y);
        }

        for (int i = 0; i < _lines.Count; i++)
        {
            renderer.DrawChars(Font, _text.Slice(_lines[i].start, _lines[i].count), transform.Matrix, _textPivot, Color, 1f, mask);
            transform.position.Y -= lineHeight;
        }

        if (_canInputText)
        {
            _canInputText = false;
            canvas.SetTextInput(RenderTransform, _textLength);
        }
    }

    public unsafe void SetText(string str)
    {
        fixed (char* p = str)
        {
            SetText(p, str.Length);
        }
    }

    public unsafe void SetText(ReadOnlySpan<char> str)
    {
        fixed (char* p = str)
        {
            SetText(p, str.Length);
        }
    }

    public unsafe void SetText(char* str, int length)
    {
        _isTmpStrReadDirty = true;
        _isTmpStrWriteDirty = false;
        _text.EnsureSizeWithoutCopy(length);

        _textLength = length;
        Line line = new Line()
        {
            start = 0
        };

        _lines.Clear();
        for (int i = 0; i < length; i++)
        {
            char c = str[i];
            GlyphInfo glyph = Font!.GetGlyph(c);
            _text[i] = c;

            //line break
            if (c == '\n' ||
            c == '\r' ||
            (_overflowHorizontal == OverflowModeHorizontal.NextLine && (line.width + glyph.Advance) * _fontSize > Size.X))
            {
                _lines.Add(line);
                Line newLine = new Line()
                {
                    start = line.start + line.count,
                };
                line = newLine;
            }

            line.count++;
            line.width += glyph.Advance;
        }

        if (line.count > 0)
        {
            _lines.Add(line);
        }
    }

    protected void RefreshTextLineBreak()
    {
        SetText(_text.Span);
    }

    public override void OnClick(Vector2 mousePosition)
    {
        base.OnClick(mousePosition);
        _canInputText = true;
    }
}