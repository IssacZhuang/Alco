namespace Alco.GUI;

/// <summary>
/// Specifies which visual channels animate when a UI control changes selectable state.
/// </summary>
[Flags]
public enum TransitionMode
{
    None,
    ColorTint = 1,
    SpriteSwap = 2,
    NodeSwap = 4,
    Transform = 8,
}