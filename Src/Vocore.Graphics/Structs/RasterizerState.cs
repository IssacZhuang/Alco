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

    public static readonly RasterizerState CullNone = new RasterizerState(FillMode.Solid, CullMode.None, FrontFace.Clockwise);
    public static readonly RasterizerState CullFront = new RasterizerState(FillMode.Solid, CullMode.Front, FrontFace.Clockwise);
    public static readonly RasterizerState CullBack = new RasterizerState(FillMode.Solid, CullMode.Back, FrontFace.Clockwise);
    public static readonly RasterizerState Wireframe = new RasterizerState(FillMode.Wireframe, CullMode.None, FrontFace.Clockwise);
}