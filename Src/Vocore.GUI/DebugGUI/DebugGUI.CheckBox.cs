using System.Numerics;
using Vocore.Graphics;

namespace Vocore.GUI;

public partial class DebugGUI
{
    /// <summary>
    /// Draw a checkbox with text
    /// </summary>
    /// <param name="text"> The text to display </param>
    /// <param name="value"> The value of the checkbox </param>
    /// <returns><c>True</c> if the value has changed</returns>
    public static bool CheckBoxWithText(string text, ref bool value)
    {
        bool isChanged = CheckBox(ref value);
        SameLine();
        Text(text);
        return isChanged;
    }

    /// <summary>
    /// Draw a checkbox
    /// </summary>
    /// <param name="value">The value of the checkbox</param>
    /// <returns><c>True</c> if the value has changed</returns>
    public static bool CheckBox(ref bool value)
    {
        bool isChanged = false;
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
                isChanged = true;
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
        return isChanged;
    }
}