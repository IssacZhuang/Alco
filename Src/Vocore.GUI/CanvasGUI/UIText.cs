using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

/// <summary>
/// The label UI node.
/// </summary>
public class UILabel : UINode
{
    private struct Line
    {
        public int start;
        public int end;
        public float width;
    }
    private readonly ArrayBuffer<char> _text = new ArrayBuffer<char>(); // for less GC
    private readonly List<Line> _lines = new List<Line>();
    private float _offsetY;
    private int _textLength;
    private string _tmpStr = string.Empty;
    private bool _isTmpStrReadDirty;
    private bool _isTmpStrWriteDirty;
    private Pivot _textPivot = Pivot.Center; // the pivot of the text relative to the container
    private TextOverflowMode _overflowMode = TextOverflowMode.None;

    public Font? Font { get; set; }
    public float FontSize { get; set; } = 16;
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

    public TextOverflowMode OverflowMode
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _overflowMode;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _overflowMode = value;
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

    public UILabel()
    {

    }

    protected unsafe override void OnUpdate(Canvas canvas, float delta)
    {
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
        transform.scale *= FontSize;
        //TODO : fix multiline vertical position
        //transform.position.Y += _offsetY;
        float lineHeight = FontSize * LineSpacing;

        BoundingBox2D mask = Mask;
        if (!HasMask)
        {
            mask = canvas.Bound;
        }

        if(_overflowMode == TextOverflowMode.Clamp)
        {
            mask = Bound;
        }

        //renderer.DrawChars(Font, _text.Slice(0, _textLength), transform.Matrix, _textPivot, Color, 1f, mask);
        
        for (int i = 0; i < _lines.Count; i++)
        {
            renderer.DrawChars(Font, _text.Slice(_lines[i].start, _lines[i].end - _lines[i].start), transform.Matrix, _textPivot, Color, 1f, mask);
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
        //_textLength = length;
        float lineWidth = 0;
        int lineStart = 0;
        int charIndex = 0;
        _lines.Clear();
        for (int i = 0; i < length; i++)
        {
            GlyphInfo glyph = Font!.GetGlyph(str[charIndex]);

            //line break
            if (str[charIndex] == '\n' ||
            str[charIndex] == '\r' ||
            (_overflowMode == TextOverflowMode.NextLine && (lineWidth + glyph.Advance) * FontSize > Size.X))
            {
                _lines.Add(new Line
                {
                    start = lineStart,
                    end = charIndex,
                    width = lineWidth
                });
                lineStart = charIndex + 1;
                lineWidth = 0;
                charIndex++;
                continue;
            }

            _text[charIndex] = str[charIndex];
            lineWidth += glyph.Advance;
            charIndex++;
        }

        //add the last line
        _lines.Add(new Line
        {
            start = lineStart,
            end = charIndex,
            width = lineWidth
        });

        _offsetY = (_lines.Count - 1) * LineSpacing * FontSize * (0.5f - TextPivot.Y);
        _textLength = charIndex;
    }


}