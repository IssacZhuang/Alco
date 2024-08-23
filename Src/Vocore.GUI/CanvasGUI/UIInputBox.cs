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
    private Vector2 _cursorPosition;
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
        if (_isSelecting)
        {
            //draw the cursor
            canvas.Renderer.DrawQuad(_cursorPosition + WorldTransform.position, CursorScale * FontSize * WorldTransform.scale, 0xffffffff, Bound);
        }
    }


    private bool TryFindTextByPosition(Vector2 mousePosition, out int line, out float charOffset, out float charAdvance, out float mouseAdvance)
    {

        if (Font == null)
        {
            line = -1;
            charOffset = 0;
            charAdvance = 0;
            mouseAdvance = 0;
            return false;
        }

        Transform2D worldTransform = WorldTransform;
        float lineHeight = FontSize * LineSpacing * worldTransform.scale.Y;
        float textWidthMultiplier = FontSize * worldTransform.scale.X;

        //bug: the text height not in used
        Vector2 textPosition = worldTransform.position + worldTransform.scale * Size * TextPivot;

        float offsetY = (_lines.Count - 1) * lineHeight * (0.5f - TextPivot.Y);
        textPosition.Y += offsetY;

        float localY = mousePosition.Y - textPosition.Y;
        line = (int)(localY / -lineHeight);

        if (line < 0 || line >= _lines.Count)
        {
            line = -1;
            charOffset = 0;
            charAdvance = 0;
            mouseAdvance = 0;
            return false;
        }

        Line textLine = _lines[line];
        float textStartX = textPosition.X - textLine.width * textWidthMultiplier * (TextPivot.X + 0.5f);
        int start = textLine.start;
        float offset = 0;
        char c = '\0';
        GlyphInfo glyph = default;
        for (int i = 0; i < textLine.count; i++)
        {
            c = _text[start + i];
            glyph = Font.GetGlyph(c);
            offset += glyph.Advance;
            if (textStartX + offset * textWidthMultiplier > mousePosition.X)
            {
                break;
            }
        }

        charOffset = (offset - glyph.Advance) * textWidthMultiplier;
        charAdvance = glyph.Advance * textWidthMultiplier;
        mouseAdvance = mousePosition.X - textStartX - charOffset;

        return true;
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
        if (TryFindTextByPosition(mousePosition, out int line, out float charOffset, out float charAdvance, out float mouseAdvance))
        {

            //DebugGUI.Text($"line: {line}, charOffset: {charOffset}, charAdvance: {charAdvance}, mouseAdvance: {mouseAdvance}");
            //get the cursor position
            _cursorPosition = new Vector2(charOffset + charAdvance * 0.5f, -line * FontSize * LineSpacing * WorldTransform.scale.Y);
        }
    }
}