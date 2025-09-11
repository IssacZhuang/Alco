using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public enum TabAction
{
    Spaces4,
    Spaces2,
    Tab
}

/// <summary>
/// The single line input box UI node.
/// </summary>
public class UIInputBox : UIText, ITextInput
{

    private struct CursorPositionRednerCache
    {
        public static readonly CursorPositionRednerCache Zero = new CursorPositionRednerCache()
        {
            line = 0,
            charOffsetInLine = 0,
        };

        public int line;
        public float charOffsetInLine;//just a cache value. the scale and font size not in used

        public override string ToString()
        {
            return $"line: {line}, charOffsetInLine: {charOffsetInLine}";
        }
    }

    private bool _isSelectionDirty;

    //the char index might be greater than the text range because of the cursor position can be at the end of the line

    private int  _selectionStartPosition;
    private CursorPositionRednerCache _selectionStartPositionCache;
    private int _selectionEndPosition;
    private CursorPositionRednerCache _selectionEndPositionCache;


    private bool _isInputAreaDirty;
    private bool _isSelecting;
    private bool _isCursorVisible;

    private float _timerCursorBlink;

    public TabAction TabAction { get; set; } = TabAction.Spaces4;

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

    private int CursorCharIndex
    {
        get
        {
            return _selectionEndPosition;
        }
    }

    private float CursorOffsetInLine
    {
        get
        {
            return _selectionEndPositionCache.charOffsetInLine;
        }
    }

    private int CursorLine
    {
        get
        {
            return GetLine(CursorCharIndex);
        }
    }

    private BoundingBox2D InputArea
    {
        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            TryRefreshCursorRenderCache();
            TryRefreshTextLineBreak();


            int line = CursorLine;
            //base on the cursor position
            if (line < 0 || Font == null)
            {
                return Bound;
            }

            Line textLine = _lines[line];
            float lineHeight = LineSpacing * FontSize;
            Transform2D transform = Transform2D.Identity;
            ReadOnlySpan<char> chars = TextSpan.Slice(textLine.start, textLine.count);

            transform.Position = Size * TextPivot;
            transform.Position.X += (CursorOffsetInLine - (0.5f + TextPivot.X) * Font.GetNormalizedTextWidth(chars)) * FontSize;
            transform.Position.Y += (_lines.Count * (0.5f - TextPivot.Y) - (line + 1.5f)) * lineHeight;
            transform.Scale = new Vector2(FontSize);

            transform = math.transform(WorldTransform, transform);

            //calculate bounding box based on the transform
            Vector2 min = transform.Position - new Vector2(0, lineHeight * 0.5f);
            Vector2 max = min + new Vector2(textLine.width * FontSize, lineHeight);
            return new BoundingBox2D(min, max);
        }
    }

    public UIInputBox() : base()
    {
        IsInteractable = true;
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);//refresh text line break

        TryRefreshCursorRenderCache();

        if (_isInputAreaDirty)
        {
            canvas.SetTextInputArea(this, InputArea, 0);
            _isInputAreaDirty = false;
        }

        if (IsEditable)
        {
            _timerCursorBlink += delta;
            if (_timerCursorBlink > CursorBlinkInterval)
            {
                _timerCursorBlink = 0;
                _isCursorVisible = !_isCursorVisible;
            }
        }

        //DebugShowLineBreak();
    }

    private int GetCursorPosition(Vector2 mousePosition)
    {

        if (Font == null)
        {
            return 0;
        }

        //use local transform
        Vector2 localMousePosition = math.tolocal(WorldTransform, mousePosition);
        Transform2D transform = Transform2D.Identity;

        float lineHeight = LineSpacing * FontSize;

        transform.Position = Size * TextPivot;
        transform.Position.Y += _lines.Count * lineHeight * (0.5f - TextPivot.Y);
        transform.Scale = new Vector2(FontSize);

        float localY = localMousePosition.Y - transform.Position.Y;


        int line = (int)(localY / -lineHeight);

        if (line < 0)
        {
            return 0;
        }

        if (line >= _lines.Count)
        {
            line = _lines.Count - 1;
        }

        Line textLine = _lines[line];
        float textStartX = transform.Position.X - textLine.width * FontSize * (TextPivot.X + 0.5f);
        int start = textLine.start;
        float offset = 0;
        char c;
        GlyphInfo glyph;

        Span<char> text = TextSpan;

        int charIndex = textLine.start + textLine.count;

        for (int i = 0; i < textLine.count; i++)
        {
            int index = start + i;
            c = text[index];
            glyph = Font.GetGlyph(c);

            offset += glyph.Advance;

            //the line break is not calculated for the cursor position
            //otherwise, the text input will be on the next line
            if (textStartX + (offset - glyph.Advance * 0.5) * FontSize > localMousePosition.X || c == '\n' || c == '\r')
            {
                charIndex = index;
                break;
            }
        }

        return charIndex;
    }

    protected override void DrawLine(Canvas canvas, int line, ReadOnlySpan<char> chars, Transform2D textLineTransform)
    {
        float textAdvances = Font!.GetNormalizedTextWidth(chars);

        if (_isSelecting)
        {
            Transform2D baseTransform = textLineTransform;
            //the left point of the text = textLineTransform.position.X + textOffsetX
            float textOffsetX = -(0.5f + TextPivot.X) * textAdvances * FontSize;

            if (CursorLine == line && _isCursorVisible && IsEditable)
            {
                Transform2D cursorTransform = baseTransform;
                cursorTransform.Position.Y -= TextPivot.Y * FontSize;
                cursorTransform.Position.X += CursorOffsetInLine * FontSize + textOffsetX;
                cursorTransform.Scale *= CursorScale;

                canvas.DrawQuad(math.transform(WorldTransform, cursorTransform).Matrix, CursorColor);
            }

            //draw selection area

            Transform2D selectionTransform = baseTransform;
            selectionTransform.Position.Y -= TextPivot.Y * FontSize;

            CursorPositionRednerCache start = _selectionStartPositionCache;
            CursorPositionRednerCache end = _selectionEndPositionCache;

            if (start.line > end.line || (start.line == end.line && start.charOffsetInLine > end.charOffsetInLine))
            {
                (end, start) = (start, end);
            }

            if (line == start.line)
            {
                float baseX = baseTransform.Position.X + textOffsetX;
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

                selectionTransform.Position.X = (selectionLeftX + selectionRightX) * 0.5f;
                selectionTransform.Scale = new Vector2(width, FontSize);

                canvas.DrawQuad(math.transform(WorldTransform, selectionTransform).Matrix, SelectionAreaColor);
            }
            else if (line > start.line && line < end.line)
            {
                float width = textAdvances * FontSize;
                selectionTransform.Position.X -= TextPivot.X * width;
                selectionTransform.Scale = new Vector2(width, FontSize);

                canvas.DrawQuad(math.transform(WorldTransform, selectionTransform).Matrix, SelectionAreaColor);
            }
            else if (end.line > start.line && line == end.line)
            {
                float width = end.charOffsetInLine * FontSize;

                selectionTransform.Position.X += textOffsetX + width * 0.5f;
                selectionTransform.Scale = new Vector2(width, FontSize);

                canvas.DrawQuad(math.transform(WorldTransform, selectionTransform).Matrix, SelectionAreaColor);
            }
        }

        base.DrawLine(canvas, line, chars, textLineTransform);
    }


    public void DeleteText(int start, int count)
    {
        Span<char> text = TextSpan;
        for (int i = start; i < text.Length - count; i++)
        {
            text[i] = text[i + count];
        }

        ResizeText(text.Length - count);
        SetLineBreakDirty();
        // IncreaseCursorPosition(-count);
    }

    /// <summary>
    /// Insert text before char at index.
    /// </summary>
    /// <param name="index"></param>
    /// <param name="str"></param>
    private void InsertText(int index, ReadOnlySpan<char> str)
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

        SetLineBreakDirty();
    }

    private void IncreaseCursorPosition(int count)
    {
        if (count == 0)
        {
            return;
        }

        _selectionEndPosition += count;
        _selectionEndPosition = math.clamp(_selectionEndPosition, 0, TextSpan.Length);
        _selectionStartPosition = _selectionEndPosition;

        SetSelectionDirty();
    }

    private CursorPositionRednerCache CalcCursorPositionRenderCache(int charIndex)
    {
        if (Font == null)
        {
            return CursorPositionRednerCache.Zero;
        }

        int lineIndex = GetLine(charIndex);
        

        if (lineIndex < 0)
        {
            return CursorPositionRednerCache.Zero;
        }


        Span<char> text = TextSpan;

        Line textLine = _lines[lineIndex];
        int charIndexInLine = charIndex - textLine.start;

        //if is after the last char in the last line
        if (lineIndex == _lines.Count - 1 && charIndexInLine >= textLine.count)
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
    private void TryRefreshCursorRenderCache()
    {
        
        if (_isSelectionDirty)
        {
            TryRefreshTextLineBreak();
            _selectionStartPositionCache = CalcCursorPositionRenderCache(_selectionStartPosition);
            _selectionEndPositionCache = CalcCursorPositionRenderCache(_selectionEndPosition);
            _isSelectionDirty = false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetSelectionDirty()
    {
        _isSelectionDirty = true;
    }

    private int GetLine(int charIndex)
    {
        //binary search
        int left = 0;
        int right = _lines.Count - 1;
        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            Line line = _lines[mid];
            if (charIndex >= line.start && charIndex < line.start + line.count)
            {
                return mid;
            }
            else if (charIndex < line.start)
            {
                right = mid - 1;
            }
            else
            {
                left = mid + 1;
            }
        }

        return _lines.Count - 1;
    }

    private void ResetBlink()
    {
        _timerCursorBlink = 0;
        _isCursorVisible = true;
    }


    #region  Handle UI Events

    public override void OnSelect(Canvas canvas, Vector2 mousePosition)
    {
        base.OnSelect(canvas, mousePosition);
        _isSelecting = true;
    }

    public override void OnDeselect(Canvas canvas, Vector2 mousePosition)
    {
        base.OnDeselect(canvas, mousePosition);
        _isSelecting = false;
    }

    public override void OnPressDown(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressDown(canvas, mousePosition);
        int position = GetCursorPosition(mousePosition);
        _selectionStartPosition = position;
        _selectionEndPosition = position;
        SetSelectionDirty();
        ResetBlink();
    }

    public override void OnPressUp(Canvas canvas, Vector2 mousePosition)
    {
        base.OnPressUp(canvas, mousePosition);
        _isInputAreaDirty = true;
        canvas.SetTextInputArea(this, InputArea, 0);
    }

    public override void OnDrag(Canvas canvas, Vector2 mousePosition)
    {
        base.OnDrag(canvas, mousePosition);
        int position = GetCursorPosition(mousePosition);
        _selectionEndPosition = position;
        SetSelectionDirty();
    }

    #endregion

    #region Handle Keyborad Events

    public void OnTextInput(Canvas canvas, ReadOnlySpan<char> text)
    {
        //replace the selected text
        int selectionStart = _selectionStartPosition;
        int selectionEnd = _selectionEndPosition;

        bool isInverted = selectionStart > selectionEnd;

        if (isInverted)
        {
            (selectionStart, selectionEnd) = (selectionEnd, selectionStart);
        }

        if (selectionStart != selectionEnd)
        {
            DeleteText(selectionStart, selectionEnd - selectionStart);
        }

        InsertText(selectionStart, text);
        _selectionEndPosition = _selectionStartPosition = selectionStart + text.Length;
        SetSelectionDirty();
        //refresh IME position
        _isInputAreaDirty = true;
        ResetBlink();
    }

    public void HandleKeyDelete()
    {
        if (_selectionStartPosition == _selectionEndPosition)
        {
            if (_selectionEndPosition < TextSpan.Length)
            {
                DeleteText(_selectionEndPosition, 1);
                SetSelectionDirty();
            }
        }
        else
        {
            DeleteSelectionText();
        }
        ResetBlink();
    }

    public void HandleKeyBackspace()
    {
        if (_selectionStartPosition == _selectionEndPosition)
        {
            if (_selectionEndPosition > 0)
            {
                DeleteText(_selectionEndPosition - 1, 1);
                IncreaseCursorPosition(-1);
                SetLineBreakDirty();
                SetSelectionDirty();
            }
        }
        else
        {
            DeleteSelectionText();
        }
        ResetBlink();
    }

    public void HandleKeyEnter()
    {
        InsertText(CursorCharIndex, "\n");
        IncreaseCursorPosition(1);
        ResetBlink();
    }

    public void HandleKeyTab()
    {
        switch(TabAction)
        {
            case TabAction.Spaces2:
                InsertText(CursorCharIndex, "  ");
                IncreaseCursorPosition(2);
                break;
            case TabAction.Spaces4:
                InsertText(CursorCharIndex, "    ");
                IncreaseCursorPosition(4);
                break;
            case TabAction.Tab:
                InsertText(CursorCharIndex, "\t");
                IncreaseCursorPosition(1);
                break;
        }
        ResetBlink();
    }

    public void HandleKeyEscape()
    {
        _selectionStartPosition = _selectionEndPosition;
        SetSelectionDirty();
        ResetBlink();
    }

    public void HandleKeyArrowLeft()
    {
        IncreaseCursorPosition(-1);
        ResetBlink();
    }

    public void HandleKeyArrowRight()
    {
        IncreaseCursorPosition(1);
        ResetBlink();
    }

    public void HandleKeyArrowUp()
    {
        if (_lines.Count == 0)
        {
            return;
        }

        int line = CursorLine;
        if (line > 0)
        {
            Line textLine = _lines[line - 1];
            float textLineOffset = textLine.width;
            float cursorOffset = CursorOffsetInLine;
            float offset = cursorOffset / textLineOffset;
            int charIndex = textLine.start + (int)math.round(textLine.count * offset);
            _selectionStartPosition = charIndex;
            _selectionEndPosition = charIndex;
            SetSelectionDirty();
        }
        ResetBlink();
    }

    public void HandleKeyArrowDown()
    {
        if (_lines.Count == 0)
        {
            return;
        }

        int line = CursorLine;
        if (line < _lines.Count - 1)
        {
            Line textLine = _lines[line + 1];
            float textLineOffset = textLine.width;
            float cursorOffset = CursorOffsetInLine;
            float offset = cursorOffset / textLineOffset;
            int charIndex = textLine.start + (int)math.round(textLine.count * offset);
            _selectionStartPosition = charIndex;
            _selectionEndPosition = charIndex;
            SetSelectionDirty();
        }

        ResetBlink();
    }

    private void DeleteSelectionText()
    {
        int selectionStart = _selectionStartPosition;
        int selectionEnd = _selectionEndPosition;

        bool isInverted = selectionStart > selectionEnd;

        if (isInverted)
        {
            (selectionStart, selectionEnd) = (selectionEnd, selectionStart);
        }

        if (selectionStart != selectionEnd)
        {
            DeleteText(selectionStart, selectionEnd - selectionStart);
        }

        _selectionEndPosition = _selectionStartPosition = selectionStart;
        SetSelectionDirty();
        //refresh IME position
        _isInputAreaDirty = true;
    }

    public void SelectAll()
    {
        _selectionStartPosition = 0;
        _selectionEndPosition = TextSpan.Length;
        SetSelectionDirty();
    }

    public Span<char> GetSelectedText()
    {
        if (_selectionStartPosition == _selectionEndPosition)
        {
            return Span<char>.Empty;
        }

        int selectionStart = _selectionStartPosition;
        int selectionEnd = _selectionEndPosition;

        if (selectionStart > selectionEnd)
        {
            (selectionStart, selectionEnd) = (selectionEnd, selectionStart);
        }

        return TextSpan.Slice(selectionStart, selectionEnd - selectionStart);
    }

    #endregion

    private void DebugShowLineBreak()
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            DebugStats.Text($"Line {i}: start: {_lines[i].start}, count: {_lines[i].count}");
        }
    }


}