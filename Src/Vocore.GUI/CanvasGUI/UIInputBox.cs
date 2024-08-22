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

    
    private void FindTextByPosition(Vector2 mousePosition)
    {
        if (Font == null)
        {
            return;
        }

        Transform2D worldTransform = WorldTransform;
        float lineHeight = FontSize * LineSpacing * worldTransform.scale.Y;
        float textWidthMultiplier = FontSize * worldTransform.scale.X;
        Vector2 textPosition = worldTransform.position + worldTransform.scale * Size * TextPivot;

        float offsetY = (_lines.Count - 1) * lineHeight * (0.5f - TextPivot.Y);
        textPosition.Y += offsetY;

        float localY = mousePosition.Y - textPosition.Y;
        int line = (int)(localY / -lineHeight);

        //DebugGUI.Text("line: " + line.ToString());

        if (line < 0 || line >= _lines.Count)
        {
            return;
        }

        Line textLine = _lines[line];
        float textStartX = textPosition.X - textLine.width * textWidthMultiplier * (TextPivot.X + 0.5f);
        int start = textLine.start;
        float offset = 0;
        char c = '\0';
        for (int i = 0; i < textLine.count; i++)
        {
            c = _text[start + i];
            GlyphInfo glyph = Font.GetGlyph(c);
            offset += glyph.Advance;
            if (textStartX + offset * textWidthMultiplier > mousePosition.X)
            {
                break;
            }
        }
        DebugGUI.Text($"c: {c}");
        //DebugGUI.Text($"textStartX: {textStartX}, {textPosition.X}, {Size.X}, {_textPivot.X}, {textLine.width}");
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
        FindTextByPosition(mousePosition);
    }
}