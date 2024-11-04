using Vocore.Graphics;

namespace Vocore.Rendering;

public sealed class ParamMaterial : Material
{
    private struct Slot
    {
        public MaterialResourceType type;
        public GPUResourceGroup? group;
    }

    private readonly Shader _shader;
    private readonly Slot[] _slots;

    public ParamMaterial(Shader shader)
    {
        _shader = shader;
        _slots = new Slot[shader.BindGroupCount];
    }



    public override GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        throw new NotImplementedException();
    }

    protected override void SetPipelineResources(MaterialCommandContext context)
    {
        
    }
}