using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

#pragma warning disable CS8618

public struct ImGuiStyle
{
    public Font Font { get; set; }
    public float FontSize { get; set; }
    public ColorFloat TextColor { get; set; }
}