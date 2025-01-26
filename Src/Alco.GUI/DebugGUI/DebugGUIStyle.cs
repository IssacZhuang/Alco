using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

#pragma warning disable CS8618

public struct DebugGUIStyle
{
    public Font Font { get; set; }
    public float FontSize { get; set; }
    public float SliderWidth { get; set; }
    public float SliderThumbWidth { get; set; }
    public ColorFloat SliderColor { get; set; }
    public ColorFloat SliderThumbColor { get; set; }
    public ColorFloat SliderThumbHoverColor { get; set; }
    public ColorFloat SliderThumbDragColor { get; set; }
    public ColorFloat TextColor { get; set; }
    public ColorFloat ButtonColor { get; set; }
    public ColorFloat ButtonHoverColor { get; set; }
    public ColorFloat ButtonPressedColor { get; set; }
    public ColorFloat CheckBoxColor { get; set; }
    public ColorFloat CheckBoxHoverColor { get; set; }
    public ColorFloat CheckBoxCheckColor { get; set; }

    /// <summary>
    /// The margin of the widget. The x,y,z,w values represent the left, right, top, and bottom margins respectively.
    /// </summary>
    public Vector4 Margin { get; set; }

    /// <summary>
    /// The text padding of text, which used by Button
    /// </summary>
    /// <value></value>
    public Vector2 Padding { get; set; }
}