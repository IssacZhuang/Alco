namespace Alco.GUI;

/// <summary>
/// The type of UI sound.
/// </summary>
[Flags]
public enum UISoundType
{
    None,
    Hover = 1 << 0,
    Click = 1 << 1,
    All = Hover | Click,
}