namespace Alco.Graphics;

/// <summary>
/// The rasterization configuration for a graphics pipeline: how triangles are
/// filled and which faces are culled.
/// </summary>
public struct RasterizerState
{
    /// <summary>
    /// Initializes the state with fill mode, cull mode and front-face winding.
    /// </summary>
    public RasterizerState(FillMode fillMode, CullMode cullMode, FrontFace frontFace)
    {
        FillMode = fillMode;
        CullMode = cullMode;
        FrontFace = frontFace;
    }
    /// <summary>
    /// How triangles are rasterized (solid or wireframe).
    /// </summary>
    public FillMode FillMode { get; init; }
    /// <summary>
    /// Which triangle faces are culled before rasterization.
    /// </summary>
    public CullMode CullMode { get; init; }
    /// <summary>
    /// The winding order considered front-facing.
    /// </summary>
    public FrontFace FrontFace { get; init; }

    /// <summary>
    /// Solid fill with no face culling.
    /// </summary>
    public static readonly RasterizerState CullNone = new RasterizerState(FillMode.Solid, CullMode.None, FrontFace.CounterClockwise);
    /// <summary>
    /// Solid fill culling front-facing triangles.
    /// </summary>
    public static readonly RasterizerState CullFront = new RasterizerState(FillMode.Solid, CullMode.Front, FrontFace.CounterClockwise);
    /// <summary>
    /// Solid fill culling back-facing triangles.
    /// </summary>
    public static readonly RasterizerState CullBack = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.CounterClockwise);
    /// <summary>
    /// Wireframe outline with no face culling.
    /// </summary>
    public static readonly RasterizerState Wireframe = new RasterizerState(FillMode.Wireframe, CullMode.None, FrontFace.CounterClockwise);

    public static bool operator ==(RasterizerState left, RasterizerState right)
    {
        return left.FillMode == right.FillMode && left.CullMode == right.CullMode && left.FrontFace == right.FrontFace;
    }

    public static bool operator !=(RasterizerState left, RasterizerState right)
    {
        return left.FillMode != right.FillMode || left.CullMode != right.CullMode || left.FrontFace != right.FrontFace;
    }

    public override bool Equals(object? obj)
    {
        return obj is RasterizerState state && this == state;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 37 + FillMode.GetHashCode();
        hash = hash * 37 + CullMode.GetHashCode();
        hash = hash * 37 + FrontFace.GetHashCode();
        return hash;
    }
}