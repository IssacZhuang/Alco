using Vocore.Graphics;

namespace Vocore.Rendering;

public struct ShaderPreproccessResultHLSLDeprecated
{

    // common
    public string ShaderText { get; set; }
    public string Filename { get; set; }
    public ShaderStage Stages { get; set; }
    public ShaderPragma[] Pragmas { get; set; }
    // graphics
    public string EntryVertex { get; set; }
    public string EntryFragment { get; set; }



    public RasterizerState? RasterizerState { get; set; }
    public BlendState? BlendState { get; set; }
    public DepthStencilState? DepthStencilState { get; set; }
    public PrimitiveTopology? PrimitiveTopology { get; set; }
    // compute
    public string EntryCompute { get; set; }
}

public struct ShaderPreproccessResult{
    public ShaderStage Stages { get; set; }
}


public struct ShaderPreproccessResultSlang
{
    public string ShaderText { get; set; }
    public string Filename { get; set; }
    public ShaderPragma[] Pragmas { get; set; }
    public RasterizerState? RasterizerState { get; set; }
    public BlendState? BlendState { get; set; }
    public DepthStencilState? DepthStencilState { get; set; }
    public PrimitiveTopology? PrimitiveTopology { get; set; }
}
