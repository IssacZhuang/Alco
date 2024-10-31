namespace Vocore.Graphics;

public struct RasterizerState
{
    public RasterizerState(FillMode fillMode, CullMode cullMode, FrontFace frontFace)
    {
        FillMode = fillMode;
        CullMode = cullMode;
        FrontFace = frontFace;
    }
    public FillMode FillMode { get; init; }
    public CullMode CullMode { get; init; }
    public FrontFace FrontFace { get; init; }

    public static readonly RasterizerState CullNone = new RasterizerState(FillMode.Solid, CullMode.None, FrontFace.CounterClockwise);
    public static readonly RasterizerState CullFront = new RasterizerState(FillMode.Solid, CullMode.Front, FrontFace.CounterClockwise);
    public static readonly RasterizerState CullBack = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.CounterClockwise);
    public static readonly RasterizerState Wireframe = new RasterizerState(FillMode.Wireframe, CullMode.None, FrontFace.CounterClockwise);

    //operator ==
    public static bool operator ==(RasterizerState left, RasterizerState right)
    {
        return left.FillMode == right.FillMode && left.CullMode == right.CullMode && left.FrontFace == right.FrontFace;
    }

    //operator !=
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