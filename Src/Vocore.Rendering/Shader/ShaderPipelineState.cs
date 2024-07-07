

using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public struct ShaderPipelineState
{
    private readonly Shader _shader;
    private GPURenderPass? _renderPass;
    private GPUPipeline? _pipeline;

    public ShaderPipelineState(Shader shader)
    {
        _shader = shader;
    }

    public GPUPipeline GetPipeline(GPURenderPass renderPass)
    {
        if (_renderPass == renderPass)
        {
            return _pipeline!;
        }
        _renderPass = renderPass;
        _pipeline = _shader.GetPipelineVariant(renderPass);
        return _pipeline;
    }
}