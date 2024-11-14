using Vocore.Graphics;

namespace Vocore.Rendering;

public class GraphicsMaterialInstance : Material
{
    private readonly GraphicsMaterial _parent;
    private readonly Shader _shader;
    private GraphicsPipelineContext _context;

    internal GraphicsMaterialInstance(GraphicsMaterial parent)
    {
        _parent = parent;
        _shader = parent.Shader;
    }

    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        _shader.TryUpdatePipelineInfo(ref _context, renderPass);
        return _context.Pipeline;
    }

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        
    }
}