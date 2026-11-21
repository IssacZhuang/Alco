

/// <summary>
/// Flags identifying which sides of a tile connect to its neighbors.
/// </summary>
[Flags]
public enum ConnectDirection
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,
}
