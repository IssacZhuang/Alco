using System.Numerics;
using Vocore.Graphics;

namespace Vocore.GUI;

public partial class DebugGUI
{
    public static void CheckBox(ref bool value)
    {
        CheckBegin();
        Vector2 drawPos = ProcessPostion();

        float height = _style.FontSize + _style.Padding.Y * 2;

        Vector2 size = new Vector2(height, height);
        drawPos.X += size.X * 0.5f;

        //hit
        Vector2 hitBoxPos = new Vector2(drawPos.X, drawPos.Y - size.Y * 0.5f );
        BoundingBox2D hitBox = new BoundingBox2D(hitBoxPos, hitBoxPos + size);

        drawPos.Y = -drawPos.Y;

        ColorFloat color = _style.CheckBoxColor;

        if (hitBox.Contains(_renderer.MousePosition))
        {
            color = _style.CheckBoxHoverColor;
            if (_renderer.IsMouseClicked)
            {
                value = !value;
            }
        }

        //bg
        _renderer.DrawQuad(drawPos, 0, size, color);

        //check mark
        if (value)
        {
            Vector2 checkSize = new Vector2(_style.FontSize, _style.FontSize);
            _renderer.DrawQuad(drawPos, 0, checkSize, _style.CheckBoxCheckColor);
        }

        SetNextOffset(new Vector2(size.X, size.Y + _style.Margin.W));

    }
}