using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class Shader : ShaderResource
{
    private readonly GPUPipeline _pipeline;
    private readonly ShaderReflectionInfo _reflectionInfo;
    private readonly Dictionary<string, uint> _resourceIds = new Dictionary<string, uint>();

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

    public bool TryGetResourceId(string name, out uint resourceId)
    {
        return _resourceIds.TryGetValue(name, out resourceId);
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

    protected override void Dispose(bool disposing)
    {
        _pipeline.Dispose();
    }

    //creation
    public static Shader CreateFromCompileResult(ShaderCompileResult result)
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
            Shader shader = new Shader(pipeline, info);

            foreach (var bindGroup in bindGroups)
            {
                bindGroup.Dispose();
            }
            return shader;
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
            Shader shader = new Shader(pipeline, info);

            foreach (var bindGroup in bindGroups)
            {
                bindGroup.Dispose();
            }
            return shader;
        }

        throw new InvalidOperationException("Invalid shader compile result");


    }
}