using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The high level encapsulation of GPU pipeline
/// </summary>
public class ShaderNew : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;

    private readonly ConcurrentDictionary<string, ShaderModulesInfo> _variantCache = new ConcurrentDictionary<string, ShaderModulesInfo>();
    private readonly ConcurrentDictionary<long, GPUPipeline> _pipelineCache = new ConcurrentDictionary<long, GPUPipeline>();

    private readonly Lock _lockCreatePipeline = new Lock();

    public string Name { get; }

    internal ShaderNew(RenderingSystem renderingSystem, string name)
    {
        _renderingSystem = renderingSystem;
        Name = name;
    }

    private GPUPipeline GetGraphicsPipeline(
        GPURenderPass renderPass,
        ShaderModulesInfo modulesInfo,
        DepthStencilState depthStencil,
        BlendState blend,
        RasterizerState rasterizer,
        PrimitiveTopology primitiveTopology
        )
    {
        long hash = default;
        //fist 32 bits are the render pass hash
        hash |= (long)renderPass.GetHashCode();

        //next 32 bits are combination of the variant hash and the pipeline state hash
        int subHash = HashCode.Combine(
            modulesInfo.GetHashCode(),
            depthStencil.GetHashCode(),
            blend.GetHashCode(),
            rasterizer.GetHashCode(),
            primitiveTopology.GetHashCode()
            );

        hash |= (long)subHash << 32;

        if (_pipelineCache.TryGetValue(hash, out GPUPipeline? pipeline))
        {
            return pipeline;
        }

        //create a new pipeline
        using (_lockCreatePipeline.EnterScope())
        {
            if (_pipelineCache.TryGetValue(hash, out GPUPipeline? pipeline2))
            {
                return pipeline2;
            }

            ShaderReflectionInfo reflectionInfo = modulesInfo.ReflectionInfo;
            GPUDevice device = _renderingSystem.GraphicsDevice;

            GPUBindGroup[] bindGroups = new GPUBindGroup[reflectionInfo.BindGroups.Count];
            for (int i = 0; i < reflectionInfo.BindGroups.Count; i++)
            {
                bindGroups[i] = device.CreateBindGroup(reflectionInfo.BindGroups[i].ToDescriptor());
            }

            GPUPipeline pipelineNew;

            if (modulesInfo.IsGraphicsShader)
            {
                PixelFormat[] colors = new PixelFormat[renderPass.Colors.Length];
                for (int i = 0; i < renderPass.Colors.Length; i++)
                {
                    colors[i] = renderPass.Colors[i].Format;
                }
                PixelFormat? depthStencilFormat = renderPass.Depth?.Format;

                GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
                    bindGroups,
                    new ShaderModule[] {
                    modulesInfo.VertexShader!.Value,
                    modulesInfo.FragmentShader!.Value
                        },
                    reflectionInfo.VertexLayouts.ToArray(),
                    rasterizer,
                    blend,
                    depthStencil,
                    primitiveTopology,
                    colors,
                    depthStencilFormat,
                    reflectionInfo.PushConstantsRanges.ToArray(),
                    Name);


                pipelineNew = device.CreateGraphicsPipeline(descriptor);
            }
            else if (modulesInfo.IsComputeShader)
            {
                ComputePipelineDescriptor descriptor = new ComputePipelineDescriptor(
                    modulesInfo.ComputeShader!.Value,
                    bindGroups,
                    Name);

                pipelineNew = device.CreateComputePipeline(descriptor);


            }
            else
            {
                throw new InvalidOperationException("The shader is neither graphics nor compute shader.");
            }

            foreach (var bindGroup in bindGroups)
            {
                bindGroup.Dispose();
            }

            _pipelineCache[hash] = pipelineNew;

            return pipelineNew;
        }
    }



    protected override void Dispose(bool disposing)
    {

    }
}