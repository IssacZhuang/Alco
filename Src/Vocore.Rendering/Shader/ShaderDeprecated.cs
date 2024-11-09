using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The high level encapsulation of GPU pipeline
/// </summary>
public class ShaderDeprecated : AutoDisposable
{

    private readonly RenderingSystem _renderingSystem;
    private readonly ConcurrentDictionary<GPURenderPass, GPUPipeline> _pipelines = new ConcurrentDictionary<GPURenderPass, GPUPipeline>();
    private ShaderCompileResultDeprecated _meta;

    private ShaderReflectionInfo _reflectionInfo;
    private FrozenDictionary<string, uint> _resourceIds = FrozenDictionary<string, uint>.Empty;

    /// <summary>
    /// Gets the shader stages.
    /// </summary>
    public ShaderStage Stages
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _meta.Stages;
    }

    /// <summary>
    /// Gets a value indicating whether this shader is a graphics shader.
    /// </summary>
    public bool IsGraphicsShader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Stages & ShaderStage.Vertex) != 0 && (Stages & ShaderStage.Fragment) != 0;
    }

    /// <summary>
    /// Gets a value indicating whether this shader is a compute shader.
    /// </summary>
    public bool IsComputeShader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Stages & ShaderStage.Compute) != 0;
    }

    /// <summary>
    /// The amount of bind groups in the shader.
    /// </summary>
    /// <value>The bind group count.</value>
    public int BindGroupCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _reflectionInfo.BindGroups.Count;
    }

    internal ShaderReflectionInfo Reflections
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _reflectionInfo;
    }

    internal ShaderDeprecated(RenderingSystem renderingSystem, ShaderCompileResultDeprecated result)
    {
        _meta = result;
        _renderingSystem = renderingSystem;
        _reflectionInfo = result.ReflectionInfo;

        BuildResourceIndex();
    }

    /// <summary>
    /// Tries to get the resource ID associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="resourceId">The resource ID if found, otherwise 0.</param>
    /// <returns>True if the resource sID was found, false otherwise.</returns>
    public bool TryGetResourceId(string name, out uint resourceId)
    {
        return _resourceIds.TryGetValue(name, out resourceId);
    }

    /// <summary>
    /// Gets the resource ID associated with the given name.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <throws>KeyNotFoundException if the resource is not found.</throws>
    /// <returns>The resource ID.</returns>
    public uint GetResourceId(string name)
    {
        if (_resourceIds.TryGetValue(name, out uint resourceId))
        {
            return resourceId;
        }
        throw new KeyNotFoundException($"Resource '{name}' not found in shader {_meta.Filename}");
    }

    /// <summary>
    /// Get the GPU pipeline for the given render pass.
    /// <br/> <c>thread safe.</c>
    /// </summary>
    /// <param name="renderPass">The render pass.</param>
    /// <returns>The GPU pipeline.</returns>
    public GPUPipeline GetPipelineVariant(GPURenderPass renderPass)
    {
        if (_pipelines.TryGetValue(renderPass, out GPUPipeline? pipeline))
        {
            return pipeline;
        }

        GPUPipeline newPipeline = _renderingSystem.CreatePipeline(_meta, renderPass);
        _pipelines[renderPass] = newPipeline;
        return newPipeline;
    }


    internal void ClearPipelineCache()
    {
        _pipelines.Clear();
    }

    private void BuildResourceIndex()
    {
        Dictionary<string, uint> resourceIds = new Dictionary<string, uint>();
        resourceIds.Clear();
        for (uint i = 0; i < _reflectionInfo.BindGroups.Count; i++)
        {
            BindGroupLayout bindGroup = _reflectionInfo.BindGroups[(int)i];
            if (bindGroup.Bindings != null
            && bindGroup.Bindings.Count > 0)
            {
                resourceIds[bindGroup.Bindings[0].Entry.Name] = i;
            }
        }

        _resourceIds = resourceIds.ToFrozenDictionary();
    }

    internal void HotReload(ShaderCompileResultDeprecated result)
    {
        _meta = result;
        _reflectionInfo = result.ReflectionInfo;

        ClearPipelineCache();

        BuildResourceIndex();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var pipeline in _pipelines.Values)
            {
                pipeline.Dispose();
            }
            _pipelines.Clear();
        }
    }
}