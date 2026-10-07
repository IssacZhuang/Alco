namespace Alco.Graphics;

/// <summary>
/// How a stencil test writes the stencil value on a given outcome.
/// </summary>
public enum StencilOperation
{
    /// <summary>Keep the existing stencil value.</summary>
    Keep = 0,
    /// <summary>Write 0.</summary>
    Zero = 1,
    /// <summary>Write the stencil reference value.</summary>
    Replace = 2,
    /// <summary>Bitwise-invert the existing stencil value.</summary>
    Invert = 3,
    /// <summary>Increment, saturating at the format's maximum.</summary>
    IncrementClamp = 4,
    /// <summary>Decrement, saturating at 0.</summary>
    DecrementClamp = 5,
    /// <summary>Increment, wrapping around to 0 past the maximum.</summary>
    IncrementWrap = 6,
    /// <summary>Decrement, wrapping around to the maximum below 0.</summary>
    DecrementWrap = 7,
}