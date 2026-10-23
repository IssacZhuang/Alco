namespace Alco.GUI;

/// <summary>
/// Flags specifying which mask sources apply to a UI node.
/// </summary>
[Flags]
public enum MaskState
{
    None = 0,
    Self = 1,
    Parent = 2,
}