using Vocore.Graphics;

namespace Vocore.Rendering;

public struct ShaderPreproccessResult
{
    public bool IsGraphicsShader
    {
        get
        {
            return !string.IsNullOrEmpty(EntryVertex) && !string.IsNullOrEmpty(EntryFragment);
        }
    }
    
    public bool IsComputeShader
    {
        get
        {
            return !string.IsNullOrEmpty(EntryCompute);
        }
    }
    // common
    public string ShaderText { get; init; }
    public ShaderPragma[] Pragmas { get; init; }
    // graphics
    public string? EntryVertex { get; init; }
    public string? EntryFragment { get; init; }
    public RasterizerState RasterizerState { get; init; }
    public BlendState BlendState { get; init; }
    public DepthStencilState DepthStencilState { get; init; }
    // compute
    public string? EntryCompute { get; init; }

}
