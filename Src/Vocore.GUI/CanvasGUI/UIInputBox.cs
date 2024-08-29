using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
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
    protected struct CursorPosition
    {
        public static readonly CursorPosition Head = new CursorPosition()
        {
            charIndex = -1,
        };
        public int line;
        public int charIndex;
        public float charOffsetInLine;//the scale and font size not in used
    }
    private CursorPosition _cursorPosition;
    private CursorPosition _selectionStartPosition;
    private CursorPosition _selectionEndPosition;
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

    public bool IsEditable { get; set; } = true;
    public float CursorBlinkInterval = 0.5f;

    public BoundingBox2D InputArea
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            //base on the cursor position
            if (_cursorPosition.line < 0)
            {
                return Bound;
            }

            Line textLine = _lines[_cursorPosition.line];
            float lineHeight = LineSpacing * FontSize;
            Transform2D transform = Transform2D.Identity;
            ReadOnlySpan<char> chars = _text.Span.Slice(textLine.start, textLine.count);
            
            transform.position = Size * TextPivot;
            transform.position.X -= (0.5f + TextPivot.X) * Font.GetNormalizedTextWidth(chars) * FontSize;
            transform.position.Y += _lines.Count * lineHeight * (0.5f - TextPivot.Y);
            transform.position.Y -= lineHeight * (_cursorPosition.line + 1.5f);
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
        base.OnUpdate(canvas, delta);
        if (IsEditable)
        {
            _timerCursorBlink += delta;
            if (_timerCursorBlink > CursorBlinkInterval)
            {
                _timerCursorBlink = 0;
                _isCursorVisible = !_isCursorVisible;
            }
        }
    }

    protected CursorPosition GetCursorPosition(Vector2 mousePosition)
    {

        if (Font == null)
        {
            return CursorPosition.Head;
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
            return CursorPosition.Head;
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
        int charIndexInLine = textLine.start - 1;
        for (int i = 0; i < textLine.count; i++)
        {
            int index = start + i;
            c = _text[index];
            glyph = Font.GetGlyph(c);

            if (textStartX + (offset + glyph.Advance * 0.5) * FontSize > localMousePosition.X)
            {
                break;
            }

            charIndexInLine = index;
            offset += glyph.Advance;
        }

        return new CursorPosition
        {
            line = line,
            charIndex = charIndexInLine,
            charOffsetInLine = offset
        }; ;
    }

    public override void OnSelect(Canvas canvas, Vector2 mousePosition)
    {
        base.OnSelect(canvas, mousePosition);
        canvas.StartTextInput(this, 0);
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
        _selectionStartPosition = GetCursorPosition(mousePosition);
    }

    public override void OnDrag(Canvas canvas, Vector2 mousePosition)
    {
        base.OnDrag(canvas, mousePosition);
        _cursorPosition = GetCursorPosition(mousePosition);
        _selectionEndPosition = _cursorPosition;
    }

    protected override void DrawLine(CanvasRenderer renderer, int line, ReadOnlySpan<char> chars, Transform2D textLineTransform, BoundingBox2D mask)
    {
        float textAdvances = Font!.GetNormalizedTextWidth(chars);

        if (_isSelecting)
        {
            Transform2D baseTransform = textLineTransform;
            //the left point of the text = textLineTransform.position.X + textOffsetX
            float textOffsetX = -(0.5f + TextPivot.X) * textAdvances * FontSize;

            if (_cursorPosition.line == line && _isCursorVisible && IsEditable)
            {
                Transform2D cursorTransform = baseTransform;
                cursorTransform.position.Y -= TextPivot.Y * FontSize * LineSpacing;
                cursorTransform.position.X += _cursorPosition.charOffsetInLine * FontSize + textOffsetX;
                cursorTransform.scale *= CursorScale;

                // if (_cursorPosition.charIndex >= 0)
                // {
                //     DebugGUI.Text($"{_text[_cursorPosition.charIndex]}");
                // }
                //DebugGUI.Text($"{_cursorPosition.charIndex}");
                renderer.DrawQuad(math.transform(WorldTransform, cursorTransform).Matrix, CursorColor, Bound);
            }

            //draw selection area

            Transform2D selectionTransform = baseTransform;
            selectionTransform.position.Y -= TextPivot.Y * FontSize;

            CursorPosition start = _selectionStartPosition;
            CursorPosition end = _selectionEndPosition;
            if (start.line > end.line || (start.line == end.line && start.charIndex > end.charIndex))
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
}