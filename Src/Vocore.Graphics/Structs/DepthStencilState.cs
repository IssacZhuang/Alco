namespace Vocore.Graphics;

public struct DepthStencilState
{
    public static readonly DepthStencilState None = new(false, CompareFunction.Never);

    public static readonly DepthStencilState Write = new(true, CompareFunction.LessEqual);

    public static readonly DepthStencilState Read = new(false, CompareFunction.LessEqual);
    public static readonly DepthStencilState Default = new(true, CompareFunction.LessEqual);
    public DepthStencilState(bool depthWriteEnabled, CompareFunction depthCompare)
    {
        DepthWriteEnabled = depthWriteEnabled;
        DepthBoundsTestEnabled = false;
        DepthCompare = depthCompare;
        FrontFace = StencilFaceState.Default;
        BackFace = StencilFaceState.Default;
    }

    public DepthStencilState(bool depthWriteEnabled, bool depthBoundsTestEnabled, CompareFunction depthCompare, StencilFaceState frontFace, StencilFaceState backFace)
    {
        DepthWriteEnabled = depthWriteEnabled;
        DepthBoundsTestEnabled = depthBoundsTestEnabled;
        DepthCompare = depthCompare;
        FrontFace = frontFace;
        BackFace = backFace;
    }

    public bool DepthWriteEnabled { get; init; }
    public bool DepthBoundsTestEnabled { get; init; }
    public CompareFunction DepthCompare { get; init; }
    public StencilFaceState FrontFace { get; init; } = StencilFaceState.Default;
    public StencilFaceState BackFace { get; init; } = StencilFaceState.Default;

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
        return HashCode.Combine(DepthWriteEnabled, DepthBoundsTestEnabled, DepthCompare, FrontFace, BackFace);
    }
}