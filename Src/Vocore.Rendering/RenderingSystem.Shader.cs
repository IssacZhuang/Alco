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
            ShaderStageSource vertex = result.VertexShader!.Value;
            ShaderStageSource fragment = result.FragmentShader!.Value;

            RasterizerState rasterizer = result.PreproccessResult.RasterizerState!.Value;
            BlendState blend = result.PreproccessResult.BlendState!.Value;
            DepthStencilState depthStencil = result.PreproccessResult.DepthStencilState!.Value;

            string filename = result.PreproccessResult.Filename;

            PixelFormat[] colors;
            PixelFormat? depthStencilFormat;

            colors = renderPass.Colors.Select(x => x.Format).ToArray();
            
            if(depthStencil.DepthCompare != CompareFunction.Never)
            {
                depthStencilFormat = renderPass.Depth?.Format;
            }
            else
            {
                depthStencilFormat = null;
            }

            GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
                bindGroups,
                new ShaderStageSource[] { vertex, fragment },
                info.VertexLayouts,
                rasterizer,
                blend,
                depthStencil,
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
        throw new InvalidOperationException($"Invalid shader type !");
    }
}