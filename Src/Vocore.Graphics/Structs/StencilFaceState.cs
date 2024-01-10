namespace Vocore.Graphics;

public struct StencilFaceState
{

    public StencilFaceState(
        CompareFunction compare,
        StencilOperation stencilFailOperation,
        StencilOperation depthFailOperation,
        StencilOperation passOperation)
    {
        Compare = compare;
        StencilFailOperation = stencilFailOperation;
        DepthFailOperation = depthFailOperation;
        PassOperation = passOperation;
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
}