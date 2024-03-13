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
    public string ShaderText { get; set; }
    public ShaderPragma[] Pragmas { get; set; }
    // graphics
    public string? EntryVertex { get; set; }
    public string? EntryFragment { get; set; }
    public RasterizerState? RasterizerState { get; set; }
    public BlendState? BlendState { get; set; }
    public DepthStencilState? DepthStencilState { get; set; }
    // compute
    public string? EntryCompute { get; set; }

}
