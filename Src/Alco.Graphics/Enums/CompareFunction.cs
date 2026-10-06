namespace Alco.Graphics;

/// <summary>
/// The comparison used by depth/stencil tests and comparison samplers: a value
/// passes when <c>reference OP value</c> holds.
/// </summary>
public enum CompareFunction
{
    /// <summary>No comparison declared (invalid where a comparison is required).</summary>
    Undefined = 0,
    /// <summary>Never passes.</summary>
    Never = 1,
    /// <summary>Passes when the reference is less than the value.</summary>
    Less = 2,
    /// <summary>Passes when the reference is less than or equal to the value.</summary>
    LessEqual = 3,
    /// <summary>Passes when the reference equals the value.</summary>
    Equal = 4,
    /// <summary>Passes when the reference is greater than the value.</summary>
    Greater = 5,
    /// <summary>Passes when the reference differs from the value.</summary>
    NotEqual = 6,
    /// <summary>Passes when the reference is greater than or equal to the value.</summary>
    GreaterEqual = 7,
    /// <summary>Always passes.</summary>
    Always = 8,
}
