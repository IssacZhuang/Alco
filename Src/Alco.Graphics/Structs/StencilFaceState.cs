namespace Alco.Graphics;

public struct StencilFaceState
{

    public StencilFaceState(
        CompareFunction compare,
        StencilOperation passOperation,
        StencilOperation depthFailOperation,
        StencilOperation stencilFailOperation
        
        )
    {
        Compare = compare;
        PassOperation = passOperation;
        StencilFailOperation = stencilFailOperation;
        DepthFailOperation = depthFailOperation;
    }
    public CompareFunction Compare { get; init; }
    public StencilOperation StencilFailOperation { get; init; }
    public StencilOperation DepthFailOperation { get; init; }
    public StencilOperation PassOperation { get; init; }

    public static readonly StencilFaceState Default = new(
        CompareFunction.Always,
        StencilOperation.Keep,
        StencilOperation.Keep,
        StencilOperation.Keep);

    public static readonly StencilFaceState Write = new(
        CompareFunction.Always,
        StencilOperation.Replace,
        StencilOperation.Keep,
        StencilOperation.Keep);

    public static readonly StencilFaceState CompareEqual = new(
        CompareFunction.Equal,
        StencilOperation.Keep,
        StencilOperation.Keep,
        StencilOperation.Keep);

    public static readonly StencilFaceState CompareNotEqual = new(
        CompareFunction.NotEqual,
        StencilOperation.Keep,
        StencilOperation.Keep,
        StencilOperation.Keep);

    public static bool operator ==(StencilFaceState left, StencilFaceState right)
    {
        return left.Compare == right.Compare &&
               left.StencilFailOperation == right.StencilFailOperation &&
               left.DepthFailOperation == right.DepthFailOperation &&
               left.PassOperation == right.PassOperation;
    }

    public static bool operator !=(StencilFaceState left, StencilFaceState right)
    {
        return left.Compare != right.Compare ||
               left.StencilFailOperation != right.StencilFailOperation ||
               left.DepthFailOperation != right.DepthFailOperation ||
               left.PassOperation != right.PassOperation;
    }

    public override bool Equals(object? obj)
    {
        return obj is StencilFaceState state && this == state;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        hash = hash * 23 + Compare.GetHashCode();
        hash = hash * 23 + StencilFailOperation.GetHashCode();
        hash = hash * 23 + DepthFailOperation.GetHashCode();
        hash = hash * 23 + PassOperation.GetHashCode();
        return hash;
    }
}