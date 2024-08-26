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
    private struct CursorPosition
    {
        public static readonly CursorPosition Empty = new CursorPosition();
        public int line;
        public int charIndexInLine;
        public float charOffsetInLine;//the scale and font size not in used
    }
    private CursorPosition _cursorPosition;
    private bool _isSelecting;

    /// <summary>
    /// The cursor scale based on the font size.
    /// </summary>
    /// <returns></returns>
    public Vector2 CursorScale = new Vector2(0.1f, 1f);

    public BoundingBox2D InputArea
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Bound;
    }

    public UIInputBox(): base()
    {
        Interactable = true;
    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);
        DebugGUI.Text(TextPivot.value.ToString());
    }

    private CursorPosition ProceesMousePosition(Vector2 mousePosition)
    {

        if (Font == null)
        {
            return CursorPosition.Empty;
        }

        Transform2D worldTransform = WorldTransform;
        float lineHeight = FontSize * LineSpacing * worldTransform.scale.Y;
        float textWidthMultiplier = FontSize * worldTransform.scale.X;

        //bug: the text height not in used
        Vector2 textPosition = worldTransform.position + worldTransform.scale * Size * TextPivot;

        float offsetY = (_lines.Count - 1) * lineHeight * (0.5f - TextPivot.Y);
        textPosition.Y += offsetY;

        float localY = mousePosition.Y - textPosition.Y;
        int line = (int)(localY / -lineHeight);

        if (line < 0 || line >= _lines.Count)
        {
            return CursorPosition.Empty;
        }

        Line textLine = _lines[line];
        float textStartX = textPosition.X - textLine.width * textWidthMultiplier * (TextPivot.X + 0.5f);
        int start = textLine.start;
        float offset = 0;
        char c = '\0';
        GlyphInfo glyph = default;
        int charIndexInLine = 0;
        for (int i = 0; i < textLine.count; i++)
        {
            charIndexInLine = start + i;
            c = _text[charIndexInLine];
            glyph = Font.GetGlyph(c);
            offset += glyph.Advance;
            if (textStartX + offset * textWidthMultiplier > mousePosition.X)
            {
                break;
            }
        }

        DebugGUI.Text(c);

        return new CursorPosition
        {
            line = line,
            charIndexInLine = charIndexInLine,
            charOffsetInLine = offset - glyph.Advance
        };
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

    public override void OnDrag(Canvas canvas, Vector2 mousePosition)
    {
        base.OnDrag(canvas, mousePosition);
        _cursorPosition = ProceesMousePosition(mousePosition);
    }

    protected override void DrawLine(CanvasRenderer renderer, int line, ReadOnlySpan<char> chars, Transform2D transform, BoundingBox2D mask)
    {
        base.DrawLine(renderer, line, chars, transform, mask);
        Transform2D cursorTransform = transform;
        cursorTransform.position.Y -= transform.scale.Y * TextPivot.Y;
        if (_isSelecting && _cursorPosition.line == line)
        {
            renderer.DrawQuad(cursorTransform.position, CursorScale * cursorTransform.scale, 0xffffffff, Bound);
        }
    }
}