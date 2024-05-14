namespace Vocore.GUI;

[Flags]
public enum TransitionMode
{
    None,
    ColorTint = 1,
    SpriteSwap = 2,
    NodeSwap = 4,
    Transform = 8,
}