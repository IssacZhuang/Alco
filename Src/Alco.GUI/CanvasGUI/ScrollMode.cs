namespace Alco.GUI;

/// <summary>
/// Specifies the scroll axes available to a scrollable UI node.
/// </summary>
[Flags]
public enum SrollMode
{
    None,
    Vertical = 1,
    Horizontal = 2,
    Both = Vertical | Horizontal
}