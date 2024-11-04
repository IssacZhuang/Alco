using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    /// <summary>
    /// Creates a shader from the compile result.
    /// </summary>
    /// <param name="result">The shader compile result.</param>
    /// <returns>The created shader.</returns>
    public Shader CreateShader(ShaderCompileResultDeprecated result)
    {
        return new Shader(this, result);
    }

    public GPUPipeline CreatePipeline(ShaderCompileResultDeprecated result, GPURenderPass renderPass)
    {
        GPUDevice device = _device;
        if (result.IsGraphicsShader)
        {
            ShaderReflectionInfo info = result.ReflectionInfo;
            GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Count];
            for (int i = 0; i < info.BindGroups.Count; i++)
            {
                bindGroups[i] = device.CreateBindGroup(info.BindGroups[i].ToDescriptor());
            }
            ShaderModule vertex = result.VertexShader!.Value;
            ShaderModule fragment = result.FragmentShader!.Value;

            RasterizerState rasterizer = result.RasterizerState!.Value;
            BlendState blend = result.BlendState!.Value;
            DepthStencilState depthStencil = result.DepthStencilState!.Value;
            PrimitiveTopology primitiveTopology = result.PrimitiveTopology!.Value;

            string filename = result.Filename;

            PixelFormat[] colors = new PixelFormat[renderPass.Colors.Length];
            for (int i = 0; i < renderPass.Colors.Length; i++)
            {
                colors[i] = renderPass.Colors[i].Format;
            }
            PixelFormat? depthStencilFormat = renderPass.Depth?.Format;

            GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
                bindGroups,
                new ShaderModule[] { vertex, fragment },
                info.VertexLayouts.ToArray(),
                rasterizer,
                blend,
                depthStencil,
                primitiveTopology,
                colors,
                depthStencilFormat,
                info.PushConstantsRanges.ToArray(),
                filename);

            //Log.Info(result.ReflectionInfo);
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
            GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Count];
            for (int i = 0; i < info.BindGroups.Count; i++)
            {
                bindGroups[i] = device.CreateBindGroup(info.BindGroups[i].ToDescriptor());
            }
            ShaderModule compute = result.ComputeShader!.Value;

            ComputePipelineDescriptor descriptor = new ComputePipelineDescriptor(
                compute,
                bindGroups,
                result.Filename);

            GPUPipeline pipeline = device.CreateComputePipeline(descriptor);

            foreach (var bindGroup in bindGroups)
            {
                bindGroup.Dispose();
            }
            return pipeline;
        }
        throw new InvalidOperationException($"Invalid shader type !");
    }
}