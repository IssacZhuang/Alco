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

    public BoundingBox2D InputArea
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Bound;
    }

    public UIInputBox(): base()
    {
        Interactable = true;
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
        Vector2 textPosition = worldTransform.position + worldTransform.scale * Size * TextPivot;

        float offsetY = (_lines.Count - 1) * lineHeight * (0.5f - TextPivot.Y);
        textPosition.Y += offsetY;

        float localY = mousePosition.Y - textPosition.Y;
        line = (int)(localY / -lineHeight);

        //DebugGUI.Text("line: " + line.ToString());

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
        //DebugGUI.Text($"c: {c}");
        //DebugGUI.Text($"textStartX: {textStartX}, {textPosition.X}, {Size.X}, {_textPivot.X}, {textLine.width}");

        return true;
    }

    public override void OnSelect(Canvas canvas, Vector2 mousePosition)
    {
        base.OnSelect(canvas, mousePosition);
        canvas.StartTextInput(this, 0);
    }

    public override void OnDeselect(Canvas canvas, Vector2 mousePosition)
    {
        base.OnDeselect(canvas, mousePosition);
        canvas.EndTextInput();
    }

    public override void OnHover(Canvas canvas, Vector2 mousePosition)
    {
        base.OnHover(canvas, mousePosition);
        if (TryFindTextByPosition(mousePosition, out int line, out float charOffset, out float charAdvance, out float mouseAdvance))
        {
            DebugGUI.Text($"line: {line}, charOffset: {charOffset}, charAdvance: {charAdvance}, mouseAdvance: {mouseAdvance}");
        }
    }
}