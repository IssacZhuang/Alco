namespace Alco.GUI;

[Flags]
public enum SrollMode
{
    None,
    Vertical = 1,
    Horizontal = 2,
    Both = Vertical | Horizontal
}