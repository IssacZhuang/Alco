using System.Runtime.CompilerServices;
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


    /// <summary>
    /// Tries to get the resource ID associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="resourceId">The resource ID if found, otherwise 0.</param>
    /// <returns>True if the resource sID was found, false otherwise.</returns>
    public readonly bool TryGetResourceId(string name, out uint resourceId)
    {
        return ModulesInfo.TryGetResourceId(name, out resourceId);
    }

    /// <summary>
    /// Gets the resource ID associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <throws>KeyNotFoundException if the resource is not found.</throws>
    /// <returns>The resource ID.</returns>
    public readonly uint GetResourceId(string name)
    {
        if (ModulesInfo.TryGetResourceId(name, out uint resourceId))
        {
            return resourceId;
        }
        throw new KeyNotFoundException($"Resource '{name}' not found in shader {ModulesInfo.Name}");
    }

    public int BindGroupCount{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ReflectionInfo.BindGroups.Count;
    }
}