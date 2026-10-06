namespace Alco.Graphics;

/// <summary>
/// Which aspect of a texture (color, depth or stencil) a view or copy addresses.
/// </summary>
public enum TextureAspect
{
    /// <summary>No aspect declared.</summary>
    None,
    /// <summary>All aspects the format provides; the only valid choice for color formats.</summary>
    All,
    /// <summary>The stencil aspect of a depth-stencil texture.</summary>
    StencilOnly,
    /// <summary>The depth aspect of a depth-stencil texture.</summary>
    DepthOnly,
}