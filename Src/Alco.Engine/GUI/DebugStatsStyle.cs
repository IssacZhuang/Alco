using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

#pragma warning disable CS8618

/// <summary>Styling used by the debug stats overlay.</summary>
public struct DebugStatsStyle
{
    /// <summary>The font used to draw text.</summary>
    public Font Font { get; set; }
    /// <summary>The font size in pixels.</summary>
    public float FontSize { get; set; }
    /// <summary>The width of slider tracks.</summary>
    public float SliderWidth { get; set; }
    /// <summary>The width of slider thumbs.</summary>
    public float SliderThumbWidth { get; set; }
    /// <summary>The color of slider tracks.</summary>
    public ColorFloat SliderColor { get; set; }
    /// <summary>The color of slider thumbs.</summary>
    public ColorFloat SliderThumbColor { get; set; }
    /// <summary>The color of slider thumbs while hovered.</summary>
    public ColorFloat SliderThumbHoverColor { get; set; }
    /// <summary>The color of slider thumbs while dragged.</summary>
    public ColorFloat SliderThumbDragColor { get; set; }
    /// <summary>The color of text.</summary>
    public ColorFloat TextColor { get; set; }
    /// <summary>The color of buttons.</summary>
    public ColorFloat ButtonColor { get; set; }
    /// <summary>The color of buttons while hovered.</summary>
    public ColorFloat ButtonHoverColor { get; set; }
    /// <summary>The color of buttons while pressed.</summary>
    public ColorFloat ButtonPressedColor { get; set; }
    /// <summary>The color of checkboxes.</summary>
    public ColorFloat CheckBoxColor { get; set; }
    /// <summary>The color of checkboxes while hovered.</summary>
    public ColorFloat CheckBoxHoverColor { get; set; }
    /// <summary>The color of checkbox check marks.</summary>
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
