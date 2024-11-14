using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public struct GraphicsPipelineContext
{
    public GPUPipeline Pipeline;
    public GPURenderPass RenderPass;
    public ShaderReflectionInfo ReflectionInfo;
    public DepthStencilState DepthStencil;
    public BlendState BlendState;
    public RasterizerState Rasterizer;
    public PrimitiveTopology PrimitiveTopology;
    public string[] Defines;


    public readonly bool TryGetResourceId(string name, out uint resourceId)
    {
        return ReflectionInfo.TryGetResourceId(name, out resourceId);
    }

    public readonly uint GetResourceId(string name)
    {
        if (ReflectionInfo.TryGetResourceId(name, out uint resourceId))
        {
            return resourceId;
        }
        throw new KeyNotFoundException($"Resource '{name}' not found in shader {Pipeline.Name}");
    }

    public int BindGroupCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ReflectionInfo.BindGroups.Count;
    }
}