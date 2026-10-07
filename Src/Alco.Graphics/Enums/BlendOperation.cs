namespace Alco.Graphics;

/// <summary>
/// How a blend equation combines the scaled source and destination colors.
/// </summary>
public enum BlendOperation
{
    /// <summary>source + destination.</summary>
    Add = 0,
    /// <summary>source - destination.</summary>
    Subtract = 1,
    /// <summary>destination - source.</summary>
    ReverseSubtract = 2,
    /// <summary>The minimum of source and destination.</summary>
    Min = 3,
    /// <summary>The maximum of source and destination.</summary>
    Max = 4,
}