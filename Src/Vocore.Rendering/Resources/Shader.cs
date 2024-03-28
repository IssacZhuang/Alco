using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The high level encapsulation of GPU pipeline
/// </summary>
public class Shader : ShaderResource
{
    private GPUPipeline _pipeline;
    private ShaderReflectionInfo _reflectionInfo;
    private readonly Dictionary<string, uint> _resourceIds = new Dictionary<string, uint>();

    /// <summary>
    /// Gets the shader stages.
    /// </summary>
    public ShaderStage Stages
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipeline.Stages;
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
    /// Gets the GPU pipeline associated with this shader.
    /// </summary>
    public GPUPipeline Pipeline
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipeline;
    }

    internal Shader(GPUPipeline pipeline, ShaderReflectionInfo reflectionInfo)
    {
        _pipeline = pipeline;
        _reflectionInfo = reflectionInfo;

        BuildResourceIndex();
    }

    /// <summary>
    /// Tries to get the resource ID associated with the given name.
    /// </summary>
    /// <param name="name">The name of the resource.</param>
    /// <param name="resourceId">The resource ID if found, otherwise 0.</param>
    /// <returns>True if the resource ID was found, false otherwise.</returns>
    public bool TryGetResourceId(string name, out uint resourceId)
    {
        return _resourceIds.TryGetValue(name, out resourceId);
    }

    /// <summary>
    /// Gets the resource ID associated with the given name.
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
        throw new KeyNotFoundException($"Resource '{name}' not found in shader");
    }

    private void BuildResourceIndex()
    {
        _resourceIds.Clear();
        for (uint i = 0; i < _reflectionInfo.BindGroups.Length; i++)
        {
            BindGroupLayout bindGroup = _reflectionInfo.BindGroups[i];
            if (bindGroup.Bindings != null
            && bindGroup.Bindings.Length > 0)
            {
                _resourceIds[bindGroup.Bindings[0].Name] = i;
            }
        }
    }

    internal void HotReload(GPUPipeline pipeline, ShaderReflectionInfo reflectionInfo)
    {
        _pipeline.Dispose();
        _pipeline = pipeline;
        _reflectionInfo = reflectionInfo;

        BuildResourceIndex();
    }

    internal void HotReload(ShaderCompileResult result)
    {
        try
        {
            GPUPipeline? pipeline = CreatePipeline(result);
            if (pipeline != null)
            {
                HotReload(pipeline, result.ReflectionInfo);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to hot reload shader: {e}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        _pipeline.Dispose();
    }

    //creation
    /// <summary>
    /// Creates a shader from the compile result.
    /// </summary>
    /// <param name="result">The shader compile result.</param>
    /// <returns>The created shader.</returns>
    public static Shader CreateFromCompileResult(ShaderCompileResult result)
    {
        GPUPipeline? pipeline = CreatePipeline(result);
        if (pipeline != null)
        {
            return new Shader(pipeline, result.ReflectionInfo);
        }

        throw new InvalidOperationException("Invalid shader compile result");
    }

    private static GPUPipeline? CreatePipeline(ShaderCompileResult result)
    {
        GPUDevice device = GetDevice();
        if (result.IsGraphicsShader)
        {
            ShaderReflectionInfo info = result.ReflectionInfo;
            GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Length];
            for (int i = 0; i < info.BindGroups.Length; i++)
            {
                bindGroups[i] = device.CreateBindGroup(info.BindGroups[i].ToDescriptor());
            }
            ShaderStageSource vertex = result.VertexShader!.Value;
            ShaderStageSource fragment = result.FragmentShader!.Value;

            RasterizerState rasterizer = result.PreproccessResult.RasterizerState!.Value;
            BlendState blend = result.PreproccessResult.BlendState!.Value;
            DepthStencilState depthStencil = result.PreproccessResult.DepthStencilState!.Value;

            string filename = result.PreproccessResult.Filename;

            GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
                bindGroups,
                new ShaderStageSource[] { vertex, fragment },
                info.VertexLayouts,
                rasterizer,
                blend,
                depthStencil,
                new PixelFormat[] { device.PrefferedSurfaceFomat },
                device.PrefferedDepthStencilFormat,
                info.PushConstantsRanges,
                filename);

            GPUPipeline pipeline = device.CreateGraphicsPipeline(descriptor);

            foreach (var bindGroup in bindGroups)
            {
                bindGroup.Dispose();
            }
            return pipeline;
        }
        else if (result.IsComputeShader)
        {
            ShaderReflectionInfo info = result.ReflectionInfo;
            GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Length];
            for (int i = 0; i < info.BindGroups.Length; i++)
            {
                bindGroups[i] = device.CreateBindGroup(info.BindGroups[i].ToDescriptor());
            }
            ShaderStageSource compute = result.ComputeShader!.Value;

            ComputePipelineDescriptor descriptor = new ComputePipelineDescriptor(
                compute,
                bindGroups,
                result.PreproccessResult.Filename);

            GPUPipeline pipeline = device.CreateComputePipeline(descriptor);

            foreach (var bindGroup in bindGroups)
            {
                bindGroup.Dispose();
            }
            return pipeline;
        }
        return null;
    }
}