namespace Vocore.Graphics;

public struct DepthStencilState
{
    public static DepthStencilState DepthNone => new(false, CompareFunction.Always);

    public static DepthStencilState DepthWrite => new(true, CompareFunction.LessEqual);

    public static DepthStencilState DepthRead => new(false, CompareFunction.LessEqual);
    public DepthStencilState(bool depthWriteEnable, CompareFunction depthCompare)
    {
        DepthWriteEnable = depthWriteEnable;
        DepthBoundsTestEnable = false;
        DepthCompare = depthCompare;
        FrontFace = StencilFaceState.Default;
        BackFace = StencilFaceState.Default;
    }

    public DepthStencilState(bool depthWriteEnable, bool depthBoundsTestEnable, CompareFunction depthCompare, StencilFaceState frontFace, StencilFaceState backFace)
    {
        DepthWriteEnable = depthWriteEnable;
        DepthBoundsTestEnable = depthBoundsTestEnable;
        DepthCompare = depthCompare;
        FrontFace = frontFace;
        BackFace = backFace;
    }

    public bool DepthWriteEnable { get; init; }
    public bool DepthBoundsTestEnable { get; init; }
    public CompareFunction DepthCompare { get; init; }
    public StencilFaceState FrontFace { get; init; } = StencilFaceState.Default;
    public StencilFaceState BackFace { get; init; } = StencilFaceState.Default;
}