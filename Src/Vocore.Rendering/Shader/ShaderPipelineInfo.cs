using Vocore.Graphics;

namespace Vocore.Rendering;

public struct ShaderPipelineInfo
{
    public GPUPipeline Pipeline;
    public GPURenderPass RenderPass;
    public ShaderModulesInfo ModulesInfo;
    public ShaderReflectionInfo ReflectionInfo;
    public DepthStencilState DepthStencil;
    public BlendState BlendState;
    public RasterizerState Rasterizer;
    public PrimitiveTopology PrimitiveTopology;
}