using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

#pragma warning disable CS8618

public struct ImGuiStyle
{
    public Font Font { get; set; }
    public float FontSize { get; set; }
    public ColorFloat TextColor { get; set; }
    public ColorFloat ButtonColor { get; set; }
    public ColorFloat ButtonHoverColor { get; set; }

    /// <summary>
    /// The margin of the imgui widget. The x,y,z,w values represent the left, right, top, and bottom margins respectively.
    /// </summary>
    public Vector4 Margin { get; set; }

    /// <summary>
    /// The text padding of text, which used by Button
    /// </summary>
    /// <value></value>
    public Vector2 Padding { get; set; }
}