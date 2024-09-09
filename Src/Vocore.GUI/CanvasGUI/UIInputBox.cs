using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

/// <summary>
/// The single line input box UI node.
/// </summary>
public class UIInputBox : UIText, ITextInput
{
    protected struct CursorPositionRednerCache
    {
        public static readonly CursorPositionRednerCache Zero = new CursorPositionRednerCache()
        {
            line = 0,
            charOffsetInLine = 0,
        };

        public int line;
        public float charOffsetInLine;//just a cache value. the scale and font size not in used
    }

    protected struct CursorPosition
    {
        public static readonly CursorPosition Zero = new CursorPosition()
        {
            line = 0,
            charIndexInLine = 0,
        };
        public int line;
        public int charIndexInLine;
    }

    private bool _isCursorOrSelectionDirty;

    //the char index might be greater than the text range because of the cursor position can be at the end of the line
    private CursorPosition _cursorPosition;
    private CursorPositionRednerCache _cursorPositionCache;
    private CursorPosition _selectionStartPosition;
    private CursorPositionRednerCache _selectionStartPositionCache;
    private CursorPosition _selectionEndPosition;
    private CursorPositionRednerCache _selectionEndPositionCache;


    private bool _isInputAreaDirty;

    private bool _isSelecting;
    private bool _isCursorVisible;
    private float _timerCursorBlink;

    /// <summary>
    /// The cursor scale based on the font size.
    /// </summary>
    /// <returns></returns>
    public Vector2 CursorScale = new Vector2(0.1f, 1f);

    /// <summary>
    /// The cursor color.
    /// </summary>
    public ColorFloat CursorColor = 0xffffff;

    /// <summary>
    /// The color of the text selection area.
    /// </summary>
    public ColorFloat SelectionAreaColor = 0x003a7a77u;

    /// <summary>
    /// It will block input when set to false.
    /// </summary>
    /// <value></value>
    public bool IsEditable { get; set; } = true;

    /// <summary>
    /// The interval of the cursor blink.
    /// </summary>
    public float CursorBlinkInterval = 0.5f;

    protected BoundingBox2D InputArea
    {
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            TryRefreshCursorPosition();
            TryRefreshTextLineBreak();

            //base on the cursor position
            if (_cursorPositionCache.line < 0 || Font == null)
            {
                return Bound;
            }

            Line textLine = _lines[_cursorPositionCache.line];
            float lineHeight = LineSpacing * FontSize;
            Transform2D transform = Transform2D.Identity;
            ReadOnlySpan<char> chars = TextSpan.Slice(textLine.start, textLine.count);

            transform.position = Size * TextPivot;
            transform.position.X += (_cursorPositionCache.charOffsetInLine - (0.5f + TextPivot.X) * Font.GetNormalizedTextWidth(chars)) * FontSize;
            transform.position.Y += (_lines.Count * (0.5f - TextPivot.Y) - (_cursorPositionCache.line + 1.5f)) * lineHeight;
            transform.scale = new Vector2(FontSize);

            transform = math.transform(WorldTransform, transform);

            //calculate bounding box based on the transform
            Vector2 min = transform.position - new Vector2(0, lineHeight * 0.5f);
            Vector2 max = min + new Vector2(textLine.width * FontSize, lineHeight);
            return new BoundingBox2D(min, max);
        }
    }

    public UIInputBox() : base()
    {
        Interactable = true;
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        TryRefreshCursorPosition();

        if (_isInputAreaDirty)
        {
            canvas.StartTextInput(this, InputArea, 0);
            _isInputAreaDirty = false;
        }

        base.OnUpdate(canvas, delta);//refresh text line break



        if (IsEditable)
        {
            _timerCursorBlink += delta;
            if (_timerCursorBlink > CursorBlinkInterval)
            {
                _timerCursorBlink = 0;
                _isCursorVisible = !_isCursorVisible;
            }
        }

        int charIndex = GetCharIndex(_cursorPosition);
        charIndex = math.clamp(charIndex, 0, TextSpan.Length - 1);

        DebugGUI.Text($"{charIndex}, {TextSpan[charIndex]}, {(int)TextSpan[charIndex]}");
        //DebugGUI.Text($"{_selectionStartPosition}, {_selectionEndPosition}");

    }

    protected CursorPosition GetCursorPosition(Vector2 mousePosition)
    {

        if (Font == null)
        {
            return CursorPosition.Zero;
        }

        //use local transform
        Vector2 localMousePosition = math.tolocal(WorldTransform, mousePosition);
        Transform2D transform = Transform2D.Identity;

        float lineHeight = LineSpacing * FontSize;

        transform.position = Size * TextPivot;
        transform.position.Y += _lines.Count * lineHeight * (0.5f - TextPivot.Y);
        transform.scale = new Vector2(FontSize);

        float localY = localMousePosition.Y - transform.position.Y;


        int line = (int)(localY / -lineHeight);

        if (line < 0)
        {
            return CursorPosition.Zero;
        }

        if (line >= _lines.Count)
        {
            line = _lines.Count - 1;
        }

        Line textLine = _lines[line];
        float textStartX = transform.position.X - textLine.width * FontSize * (TextPivot.X + 0.5f);
        int start = textLine.start;
        float offset = 0;
        char c;
        GlyphInfo glyph;

        Span<char> text = TextSpan;

        int charIndex = textLine.start;
        charIndex = textLine.start + textLine.count;

        for (int i = 0; i < textLine.count; i++)
        {
            int index = start + i;
            c = text[index];
            glyph = Font.GetGlyph(c);

            offset += glyph.Advance;

            if (textStartX + (offset - glyph.Advance * 0.5) * FontSize > localMousePosition.X)
            {
                charIndex = index;
                break;
            }
        }


        return new CursorPosition
        {
            line = line,
            charIndexInLine = charIndex - textLine.start
        };
    }

    public override void OnSelect(Canvas canvas, Vector2 mousePosition)
    {
        base.OnSelect(canvas, mousePosition);
        _isSelecting = true;
    }

    public override void OnDeselect(Canvas canvas, Vector2 mousePosition)
    {
        base.OnDeselect(canvas, mousePosition);
        canvas.EndTextInput();
        _isSelecting = false;
    }

    public override void OnPressDown(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressDown(canvas, mousePosition);
        _cursorPosition = GetCursorPosition(mousePosition);
        _selectionStartPosition = _cursorPosition;
        _selectionEndPosition = _cursorPosition;
        SetCursorPositionOrSelectionDirty();
    }

    public override void OnPressUp(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressUp(canvas, mousePosition);
        _isInputAreaDirty = true;
        canvas.StartTextInput(this, InputArea, 0);
    }

    public override void OnDrag(Canvas canvas, Vector2 mousePosition)
    {
        base.OnDrag(canvas, mousePosition);
        // _cursorPositionCache = GetCursorPosition(mousePosition);
        // _selectionEndPositionCache = _cursorPositionCache;
        _cursorPosition = GetCursorPosition(mousePosition);
        _selectionEndPosition = _cursorPosition;
        SetCursorPositionOrSelectionDirty();
    }

    public void OnTextInput(Canvas canvas, string text)
    {
        //replace the selected text
        // int selectionStart = _selectionStartPosition;
        // int selectionEnd = _selectionEndPosition;
        int selectionStart = GetCharIndex(_selectionStartPosition);
        int selectionEnd = GetCharIndex(_selectionEndPosition);
        int cursorCharIndex = GetCharIndex(_cursorPosition);

        bool isInverted = selectionStart > selectionEnd;

        if (isInverted)
        {
            (selectionStart, selectionEnd) = (selectionEnd, selectionStart);
        }


        if (selectionStart == selectionEnd)
        {
            InsertText(cursorCharIndex, text);
        }
        else
        {
            if (isInverted)
            {
                //_cursorPosition = selectionEnd;
                _cursorPosition = _selectionStartPosition;
            }

            DeleteText(selectionStart, selectionEnd - selectionStart);
            InsertText(selectionStart, text);
            _selectionStartPosition = CursorPosition.Zero;
            _selectionEndPosition = CursorPosition.Zero;
        }

        SetLineBreakDirty();

        IncreaseCursorPosition(text.Length);
        //refresh IME position
        _isInputAreaDirty = true;
        //canvas.StartTextInput(this, 0);
    }

    protected override void DrawLine(CanvasRenderer renderer, int line, ReadOnlySpan<char> chars, Transform2D textLineTransform, BoundingBox2D mask)
    {
        float textAdvances = Font!.GetNormalizedTextWidth(chars);

        if (_isSelecting)
        {
            Transform2D baseTransform = textLineTransform;
            //the left point of the text = textLineTransform.position.X + textOffsetX
            float textOffsetX = -(0.5f + TextPivot.X) * textAdvances * FontSize;

            if (_cursorPositionCache.line == line && _isCursorVisible && IsEditable)
            {
                Transform2D cursorTransform = baseTransform;
                cursorTransform.position.Y -= TextPivot.Y * FontSize;
                cursorTransform.position.X += _cursorPositionCache.charOffsetInLine * FontSize + textOffsetX;
                cursorTransform.scale *= CursorScale;

                renderer.DrawQuad(math.transform(WorldTransform, cursorTransform).Matrix, CursorColor, Bound);
            }

            //draw selection area

            Transform2D selectionTransform = baseTransform;
            selectionTransform.position.Y -= TextPivot.Y * FontSize;

            CursorPositionRednerCache start = _selectionStartPositionCache;
            CursorPositionRednerCache end = _selectionEndPositionCache;

            if (start.line > end.line || (start.line == end.line && start.charOffsetInLine > end.charOffsetInLine))
            {
                (end, start) = (start, end);
            }

            if (line == start.line)
            {
                float baseX = baseTransform.position.X + textOffsetX;
                float selectionLeftX = baseX + start.charOffsetInLine * FontSize;

                float width;
                float selectionRightX;

                if (line == end.line)
                {
                    selectionRightX = baseX + end.charOffsetInLine * FontSize;
                    width = (end.charOffsetInLine - start.charOffsetInLine) * FontSize;
                }
                else
                {
                    selectionRightX = baseX + textAdvances * FontSize;
                    width = (textAdvances - start.charOffsetInLine) * FontSize;
                }

                selectionTransform.position.X = (selectionLeftX + selectionRightX) * 0.5f;
                selectionTransform.scale = new Vector2(width, FontSize);

                renderer.DrawQuad(math.transform(WorldTransform, selectionTransform).Matrix, SelectionAreaColor, Bound);
            }
            else if (line > start.line && line < end.line)
            {
                float width = textAdvances * FontSize;
                selectionTransform.position.X -= TextPivot.X * width;
                selectionTransform.scale = new Vector2(width, FontSize);

                renderer.DrawQuad(math.transform(WorldTransform, selectionTransform).Matrix, SelectionAreaColor, Bound);
            }
            else if (end.line > start.line && line == end.line)
            {
                float width = end.charOffsetInLine * FontSize;

                selectionTransform.position.X += textOffsetX + width * 0.5f;
                selectionTransform.scale = new Vector2(width, FontSize);

                renderer.DrawQuad(math.transform(WorldTransform, selectionTransform).Matrix, SelectionAreaColor, Bound);
            }
        }

        base.DrawLine(renderer, line, chars, textLineTransform, mask);
    }


    protected void DeleteText(int start, int count)
    {
        Span<char> text = TextSpan;
        for (int i = start; i < text.Length - count; i++)
        {
            text[i] = text[i + count];
        }

        IncreaseCursorPosition(-count);
        ResizeText(text.Length - count);
    }

    /// <summary>
    /// Insert text before char at index.
    /// </summary>
    /// <param name="index"></param>
    /// <param name="str"></param>
    protected void InsertText(int index, ReadOnlySpan<char> str)
    {
        Span<char> text = TextSpan;
        int originalLength = text.Length;
        text = ResizeText(text.Length + str.Length);

        for (int i = originalLength - 1; i >= index; i--)
        {
            text[i + str.Length] = text[i];
        }

        for (int i = 0; i < str.Length; i++)
        {
            text[index + i] = str[i];
        }

    }

    protected void IncreaseCursorPosition(int count)
    {
        if (count == 0)
        {
            return;
        }



        Line textLine = _lines[_cursorPosition.line];
        int charIndexInLine = _cursorPosition.charIndexInLine + count;
        if (count > 0)
        {
            while (charIndexInLine > textLine.count)
            {
                charIndexInLine -= textLine.count;
                _cursorPosition.line++;
                if (_cursorPosition.line >= _lines.Count)
                {
                    _cursorPosition.line = _lines.Count - 1;
                    break;
                }
                textLine = _lines[_cursorPosition.line];
            }
        }
        else
        {
            while (charIndexInLine < 0)
            {
                _cursorPosition.line--;
                if (_cursorPosition.line < 0)
                {
                    _cursorPosition.line = 0;
                    charIndexInLine = 0;
                    break;
                }
                textLine = _lines[_cursorPosition.line];
                charIndexInLine += textLine.count;
            }
        }

        _cursorPosition.charIndexInLine = charIndexInLine;
        SetCursorPositionOrSelectionDirty();
    }

    protected CursorPositionRednerCache CalcCursorPositionRenderCache(CursorPosition cursor)
    {
        if (Font == null)
        {
            return CursorPositionRednerCache.Zero;
        }

        if (cursor.charIndexInLine < 0 || cursor.line < 0)
        {
            return CursorPositionRednerCache.Zero;
        }

        int lineIndex = cursor.line;
        Span<char> text = TextSpan;

        Line textLine = _lines[lineIndex];
        int charIndexInLine = cursor.charIndexInLine;

        //if is after the last char in the last line
        if (lineIndex == _lines.Count - 1 && charIndexInLine > textLine.count)
        {
            return new CursorPositionRednerCache
            {
                line = lineIndex,
                charOffsetInLine = Font.GetNormalizedTextWidth(text.Slice(textLine.start, textLine.count))
            };
        }

        return new CursorPositionRednerCache
        {
            line = lineIndex,
            charOffsetInLine = Font.GetNormalizedTextWidth(text.Slice(textLine.start, charIndexInLine))
        };
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void TryRefreshCursorPosition()
    {
        if (_isCursorOrSelectionDirty)
        {
            _cursorPositionCache = CalcCursorPositionRenderCache(_cursorPosition);
            _selectionStartPositionCache = CalcCursorPositionRenderCache(_selectionStartPosition);
            _selectionEndPositionCache = CalcCursorPositionRenderCache(_selectionEndPosition);
            _isCursorOrSelectionDirty = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void SetCursorPositionOrSelectionDirty()
    {
        _isCursorOrSelectionDirty = true;
    }

    protected int GetCharIndex(CursorPosition cursor)
    {
        return _lines[cursor.line].start + cursor.charIndexInLine;
    }
}