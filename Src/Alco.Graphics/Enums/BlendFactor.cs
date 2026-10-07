namespace Alco.Graphics;

/// <summary>
/// The source a blend equation multiplies the source or destination color by.
/// </summary>
public enum BlendFactor
{
    /// <summary>The constant 0.</summary>
    Zero = 0,
    /// <summary>The constant 1.</summary>
    One = 1,
    /// <summary>The (pre-blend) source color.</summary>
    Src = 2,
    /// <summary>1 minus the source color.</summary>
    OneMinusSrc = 3,
    /// <summary>The source alpha, replicated to all channels.</summary>
    SrcAlpha = 4,
    /// <summary>1 minus the source alpha, replicated to all channels.</summary>
    OneMinusSrcAlpha = 5,
    /// <summary>The (pre-blend) destination color.</summary>
    Dst = 6,
    /// <summary>1 minus the destination color.</summary>
    OneMinusDst = 7,
    /// <summary>The destination alpha, replicated to all channels.</summary>
    DstAlpha = 8,
    /// <summary>1 minus the destination alpha, replicated to all channels.</summary>
    OneMinusDstAlpha = 9,
    /// <summary>
    /// The source alpha clamped so source and destination contributions sum to at
    /// most 1 (the classic OpenGL <c>GL_SRC_ALPHA_SATURATE</c>).
    /// </summary>
    SrcAlphaSaturated = 10,
    /// <summary>
    /// A constant blend color supplied by the pipeline (the alpha component in
    /// alpha equations).
    /// </summary>
    Constant = 11,
    /// <summary>1 minus the constant blend color.</summary>
    OneMinusConstant = 12,
}