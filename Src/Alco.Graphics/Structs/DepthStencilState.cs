namespace Alco.Graphics;

/// <summary>
/// Represents the depth and stencil state configuration for graphics rendering.
/// </summary>
public struct DepthStencilState
{
    /// <summary>
    /// A depth-stencil state with depth testing disabled and stencil disabled.
    /// </summary>
    public static readonly DepthStencilState None = new(false, CompareFunction.Never);

    /// <summary>
    /// A depth-stencil state that writes depth and uses a standard depth comparison (LessEqual).
    /// </summary>
    public static readonly DepthStencilState Write = new(true, CompareFunction.LessEqual);

    /// <summary>
    /// A depth-stencil state that reads depth (using LessEqual comparison) but does not write depth.
    /// </summary>
    public static readonly DepthStencilState Read = new(false, CompareFunction.LessEqual);

    /// <summary>
    /// The default depth-stencil state, the depth test is always pass.
    /// </summary>
    public static readonly DepthStencilState Default = new(false, CompareFunction.Always);

    /// <summary>
    /// Initializes a new instance of the <see cref="DepthStencilState"/> struct.
    /// </summary>
    /// <param name="depthWriteEnabled">Whether depth writing is enabled.</param>
    /// <param name="depthCompare">The depth comparison function.</param>
    public DepthStencilState(bool depthWriteEnabled, CompareFunction depthCompare)
    {
        DepthWriteEnabled = depthWriteEnabled;
        DepthBoundsTestEnabled = false;
        DepthCompare = depthCompare;
        FrontFace = StencilFaceState.Default;
        BackFace = StencilFaceState.Default;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DepthStencilState"/> struct.
    /// </summary>
    /// <param name="depthWriteEnabled">Whether depth writing is enabled.</param>
    /// <param name="depthBoundsTestEnabled">Whether depth bounds test is enabled.</param>
    /// <param name="depthCompare">The depth comparison function.</param>
    /// <param name="frontFace">The stencil state for front-facing polygons.</param>
    /// <param name="backFace">The stencil state for back-facing polygons.</param>
    public DepthStencilState(bool depthWriteEnabled, bool depthBoundsTestEnabled, CompareFunction depthCompare, StencilFaceState frontFace, StencilFaceState backFace)
    {
        DepthWriteEnabled = depthWriteEnabled;
        DepthBoundsTestEnabled = depthBoundsTestEnabled;
        DepthCompare = depthCompare;
        FrontFace = frontFace;
        BackFace = backFace;
    }

    /// <summary>
    /// Gets whether depth writing is enabled.
    /// </summary>
    public bool DepthWriteEnabled { get; init; }

    /// <summary>
    /// Gets whether depth bounds test is enabled.
    /// </summary>
    public bool DepthBoundsTestEnabled { get; init; }

    /// <summary>
    /// Gets the depth comparison function.
    /// </summary>
    public CompareFunction DepthCompare { get; init; }

    /// <summary>
    /// Gets the stencil state for front-facing polygons.
    /// </summary>
    public StencilFaceState FrontFace { get; init; } = StencilFaceState.Default;

    /// <summary>
    /// Gets the stencil state for back-facing polygons.
    /// </summary>
    public StencilFaceState BackFace { get; init; } = StencilFaceState.Default;

    /// <summary>
    /// Gets the stencil write mask.
    /// </summary>
    public uint StencilWriteMask { get; init; }

    /// <summary>
    /// Gets the stencil read mask.
    /// </summary>
    public uint StencilReadMask { get; init; }

    public static bool operator ==(DepthStencilState left, DepthStencilState right)
    {
        return left.DepthWriteEnabled == right.DepthWriteEnabled &&
               left.DepthBoundsTestEnabled == right.DepthBoundsTestEnabled &&
               left.DepthCompare == right.DepthCompare &&
               left.FrontFace == right.FrontFace &&
               left.BackFace == right.BackFace;
    }

    public static bool operator !=(DepthStencilState left, DepthStencilState right)
    {
        return left.DepthWriteEnabled != right.DepthWriteEnabled ||
               left.DepthBoundsTestEnabled != right.DepthBoundsTestEnabled ||
               left.DepthCompare != right.DepthCompare ||
               left.FrontFace != right.FrontFace ||
               left.BackFace != right.BackFace;
    }

    public override bool Equals(object? obj)
    {
        return obj is DepthStencilState state && this == state;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            DepthWriteEnabled, 
            DepthBoundsTestEnabled, 
            DepthCompare, 
            FrontFace, 
            BackFace,
            StencilReadMask,
            StencilWriteMask
            );
    }
}