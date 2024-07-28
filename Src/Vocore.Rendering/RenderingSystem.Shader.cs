using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    /// <summary>
    /// Creates a shader from the compile result.
    /// </summary>
    /// <param name="result">The shader compile result.</param>
    /// <returns>The created shader.</returns>
    public Shader CreateShader(ShaderCompileResult result)
    {
        return new Shader(this, result);
    }

    public GPUPipeline CreatePipeline(ShaderCompileResult result, GPURenderPass renderPass)
    {
        GPUDevice device = _device;
        if (result.IsGraphicsShader)
        {
            ShaderReflectionInfo info = result.ReflectionInfo;
            GPUBindGroup[] bindGroups = new GPUBindGroup[info.BindGroups.Length];
            for (int i = 0; i < info.BindGroups.Length; i++)
            {
                bindGroups[i] = device.CreateBindGroup(info.BindGroups[i].ToDescriptor());
            }
            ShaderModule vertex = result.VertexShader!.Value;
            ShaderModule fragment = result.FragmentShader!.Value;

            RasterizerState rasterizer = result.PreproccessResult.RasterizerState!.Value;
            BlendState blend = result.PreproccessResult.BlendState!.Value;
            DepthStencilState depthStencil = result.PreproccessResult.DepthStencilState!.Value;
            PrimitiveTopology primitiveTopology = result.PreproccessResult.PrimitiveTopology!.Value;

            string filename = result.PreproccessResult.Filename;

            PixelFormat[] colors = renderPass.Colors.Select(x => x.Format).ToArray();
            PixelFormat? depthStencilFormat = renderPass.Depth?.Format;

            GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
                bindGroups,
                new ShaderModule[] { vertex, fragment },
                info.VertexLayouts,
                rasterizer,
                blend,
                depthStencil,
                primitiveTopology,
                colors,
                depthStencilFormat,
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
            ShaderModule compute = result.ComputeShader!.Value;

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
        throw new InvalidOperationException($"Invalid shader type !");
    }
}