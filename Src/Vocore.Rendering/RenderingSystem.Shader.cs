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
        GPUPipeline? pipeline = CreatePipeline(result);
        if (pipeline != null)
        {
            return new Shader(pipeline, result.ReflectionInfo);
        }

        throw new InvalidOperationException("Invalid shader compile result");
    }

    private GPUPipeline? CreatePipeline(ShaderCompileResult result)
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
            if (result.PreproccessResult.RenderPass.IsNullOrEmpty())
            {
                colors = new PixelFormat[] { device.PrefferedSurfaceFomat };
                depthStencilFormat = device.PrefferedDepthStencilFormat;
            }
            else
            {
                GPURenderPass renderPass = GetRenderPass(result.PreproccessResult.RenderPass!);
                colors = renderPass.Colors.Select(x => x.Format).ToArray();
                depthStencilFormat = renderPass.Depth?.Format;
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
        return null;
    }
}