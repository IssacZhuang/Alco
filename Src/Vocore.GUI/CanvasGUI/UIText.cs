using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

/// <summary>
/// The label UI node.
/// </summary>
public class UIText : UISelectable
{
    public const int MinSpanFormattableSize = 32;
    protected struct Line
    {
        public int start;
        public int count;
        public float width;
    }
    private readonly ArrayBuffer<char> _text = new ArrayBuffer<char>(); // for less GC
    protected readonly List<Line> _lines = new List<Line>();
    private bool _isLineBreakDirty;
    private int _textLength;
    private float _fontSize = 16f;
    private string _tmpStr = string.Empty;
    private bool _isTmpStrReadDirty;
    private bool _isTmpStrWriteDirty;
    private Pivot _textPivot = Pivot.Center; // the pivot of the text relative to the container
    private OverflowModeHorizontal _overflowHorizontal = OverflowModeHorizontal.None;
    private OverflowModeVertical _overflowVertical = OverflowModeVertical.None;

    /// <summary>
    /// The font for rendering text. The text will not display if the font is null.
    /// </summary>
    /// <value></value>
    public Font? Font { get; set; }

    /// <summary>
    /// The font size for rendering text.
    /// </summary>
    /// <value></value>
    public float FontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            if (_overflowHorizontal == OverflowModeHorizontal.NextLine)
            {
                TryRefreshTextLineBreak();
            }
        }
    }

    /// <summary>
    /// The normalized line spacing, the line height is FontSize * LineSpacing.
    /// </summary>
    /// <value></value>
    public float LineSpacing { get; set; } = 1f;

    /// <summary>
    /// The text color.
    /// </summary>
    /// <value></value>
    public ColorFloat Color { get; set; } = 0xffffff;

    /// <summary>
    /// The text data.
    /// </summary>
    /// <value></value>
    public Span<char> TextSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Span<char> span = _text.Data;
            return span.Slice(0, _textLength);
        }
    }

    /// <summary>
    /// The text string.
    /// </summary>
    /// <value></value>
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

    /// <summary>
    /// The text pivot relative to the self container.
    /// </summary>
    /// <value></value>
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

    public UIText()
    {
        //default false, override by InputBox
        Interactable = false;
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

        if (_isLineBreakDirty)
        {
            TryRefreshTextLineBreak();
        }

        if (_textLength == 0)
        {
            return;
        }

        CanvasRenderer renderer = canvas.Renderer;

        //use local transform
        Transform2D transform = Transform2D.Identity;
        transform.position = Size * TextPivot;
        transform.scale = new Vector2(FontSize);
        float lineHeight = LineSpacing* FontSize;
        float offsetY = (_lines.Count - 1) * lineHeight * (0.5f - TextPivot.Y);
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
            //renderer.DrawChars(Font, _text.Slice(_lines[i].start, _lines[i].count), transform.Matrix, _textPivot, Color, 1f, mask);
            DrawLine(renderer, i, _text.Slice(_lines[i].start, _lines[i].count), transform, mask);
            transform.position.Y -= lineHeight;
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
        for (int i = 0; i < length; i++)
        {
            _text[i] = str[i];
        }

        SetLineBreakDirty();
    }

    protected virtual void DrawLine(CanvasRenderer renderer, int line, ReadOnlySpan<char> chars, Transform2D textLineTransform, BoundingBox2D mask)
    {
        renderer.DrawChars(Font!, chars, math.transform(WorldTransform, textLineTransform).Matrix, TextPivot, Color, 1f, mask);
    }

    protected void SetLineBreakDirty()
    {
        _isLineBreakDirty = true;
    }

    protected void TryRefreshTextLineBreak()
    {
        if (!_isLineBreakDirty)
        {
            return;
        }
        _isLineBreakDirty = false;
        Span<char> text = TextSpan;
        Line line = new Line()
        {
            start = 0
        };

        _lines.Clear();

        if (_textLength == 0)
        {
            return;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            GlyphInfo glyph = Font!.GetGlyph(c);


            //line break
            if (_overflowHorizontal == OverflowModeHorizontal.NextLine && (line.width + glyph.Advance) * _fontSize > Size.X)
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

            if (c == '\n' ||
            c == '\r')
            {
                _lines.Add(line);
                Line newLine = new Line()
                {
                    start = line.start + line.count,
                };
                line = newLine;
            }
        }

        if (line.count > 0)
        {
            _lines.Add(line);
        }
    }

    protected Span<char> ResizeText(int length)
    {
        _text.EnsureSize(length);
        _textLength = length;
        return TextSpan;
    }
}