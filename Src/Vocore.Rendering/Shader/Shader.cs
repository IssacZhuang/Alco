using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The high level encapsulation of GPU pipeline
/// </summary>
public class Shader : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly ConcurrentDictionary<int, ShaderModulesInfo> _modulesCache = new ConcurrentDictionary<int, ShaderModulesInfo>();
    private readonly ConcurrentDictionary<long, GPUPipeline> _graphicsPipelineCache = new ConcurrentDictionary<long, GPUPipeline>();
    private readonly ConcurrentDictionary<ShaderModulesInfo, GPUPipeline> _computePipelineCache = new ConcurrentDictionary<ShaderModulesInfo, GPUPipeline>();

    private readonly Lock _lockCreateGraphicsPipeline = new Lock();
    private readonly Lock _lockCreateComputePipeline = new Lock();
    private readonly Lock _lockCreateModules = new Lock();
    private readonly string _shaderText;

    /// <summary>
    /// The name of the shader
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Create a new shader
    /// </summary>
    /// <param name="renderingSystem">The rendering system</param>
    /// <param name="shaderText">The shader text</param>
    /// <param name="name">The name of the shader</param>
    internal Shader(RenderingSystem renderingSystem, string shaderText, string name)
    {
        _renderingSystem = renderingSystem;
        _shaderText = shaderText;
        Name = name;

        //default permutation
        int hash = GetDefinesHash(ReadOnlySpan<string>.Empty);
        _modulesCache[hash] = UtilsShaderHLSL.Compile(shaderText, name, ReadOnlySpan<string>.Empty);
    }

    public GraphicsPipelineContext GetGraphicsPipeline(
        GPURenderPass renderPass,
        DepthStencilState depthStencil,
        BlendState blend,
        RasterizerState rasterizer,
        PrimitiveTopology primitiveTopology,
        params ReadOnlySpan<string> defines
        )
    {
        ShaderModulesInfo modulesInfo = GetShaderModules(defines);
        GPUPipeline pipeline = GetGraphicsPipeline(renderPass, modulesInfo, depthStencil, blend, rasterizer, primitiveTopology);
        return new GraphicsPipelineContext
        {
            Pipeline = pipeline,
            RenderPass = renderPass,
            ReflectionInfo = modulesInfo.ReflectionInfo,
            DepthStencil = depthStencil,
            BlendState = blend,
            Rasterizer = rasterizer,
            PrimitiveTopology = primitiveTopology,
            Defines = defines.ToArray()
        };
    }

    public GraphicsPipelineContext GetGraphicsPipeline(
        GPURenderPass renderPass,
        DepthStencilState depthStencil,
        BlendState blend,
        params ReadOnlySpan<string> defines
        )
    {
        return GetGraphicsPipeline(
            renderPass,
            depthStencil,
            blend,
            RasterizerState.CullNone,
            PrimitiveTopology.TriangleList,
            defines
            );
    }


    public bool TryUpdatePipelineContext(ref GraphicsPipelineContext pipelineInfo, GPURenderPass renderPass, bool forced = false)
    {
        if (pipelineInfo.RenderPass == renderPass && !forced)
        {
            return false;
        }

        ShaderModulesInfo modulesInfo = GetShaderModules(pipelineInfo.Defines);

        GPUPipeline pipeline = GetGraphicsPipeline(
            renderPass,
            modulesInfo,
            pipelineInfo.DepthStencil,
            pipelineInfo.BlendState,
            pipelineInfo.Rasterizer,
            pipelineInfo.PrimitiveTopology
            );

        pipelineInfo.Pipeline = pipeline;
        pipelineInfo.RenderPass = renderPass;
        pipelineInfo.ReflectionInfo = modulesInfo.ReflectionInfo;

        return true;
    }

    public ComputePipelineContext GetComputePipelineInfo(params ReadOnlySpan<string> defines)
    {
        ShaderModulesInfo modulesInfo = GetShaderModules(defines);
        GPUPipeline pipeline = GetComputePipeline(modulesInfo);
        return new ComputePipelineContext
        {
            Pipeline = pipeline,
            ReflectionInfo = modulesInfo.ReflectionInfo
        };
    }

    public ShaderModulesInfo GetShaderModules(params ReadOnlySpan<string> defines)
    {
        int hash = GetDefinesHash(defines);

        if (_modulesCache.TryGetValue(hash, out ShaderModulesInfo? modulesInfo))
        {
            return modulesInfo;
        }

        //throw new Exception($"ShaderModulesInfo not found for defines: {string.Join(", ", defines)}");
        using (_lockCreateModules.EnterScope())
        {
            if (_modulesCache.TryGetValue(hash, out ShaderModulesInfo? modulesInfo2))
            {
                return modulesInfo2;
            }

            modulesInfo = UtilsShaderHLSL.Compile(_shaderText, Name, defines);
            _modulesCache[hash] = modulesInfo;

            return modulesInfo;
        }
    }

    private int GetDefinesHash(ReadOnlySpan<string> defines)
    {
        int length = defines.Length + 4;//reserve space for |
        for (int i = 0; i < defines.Length; i++)
        {
            string? define = defines[i];
            if (define != null)
            {
                length += define.Length;
            }
        }

        Span<char> buffer = stackalloc char[length];
        int index = 0;
        for (int i = 0; i < defines.Length; i++)
        {
            buffer[index++] = '|';
            string? define = defines[i];
            if (define != null)
            {
                define.CopyTo(buffer.Slice(index));
                index += define.Length;
            }
        }

        return string.GetHashCode(buffer.Slice(0, index));
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

        if (_graphicsPipelineCache.TryGetValue(hash, out GPUPipeline? pipeline))
        {
            return pipeline;
        }

        //create a new pipeline
        using (_lockCreateGraphicsPipeline.EnterScope())
        {
            if (_graphicsPipelineCache.TryGetValue(hash, out GPUPipeline? pipeline2))
            {
                return pipeline2;
            }

            if (!modulesInfo.IsGraphicsShader)
            {
                throw new InvalidOperationException("Trying to create a graphics pipeline from a non-graphics shader modules.");
            }

            ShaderReflectionInfo reflectionInfo = modulesInfo.ReflectionInfo;
            GPUDevice device = _renderingSystem.GraphicsDevice;

            GPUBindGroup[] bindGroups = new GPUBindGroup[reflectionInfo.BindGroups.Count];
            for (int i = 0; i < reflectionInfo.BindGroups.Count; i++)
            {
                bindGroups[i] = device.CreateBindGroup(reflectionInfo.BindGroups[i].ToDescriptor());
            }

            GPUPipeline pipelineNew;


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


            foreach (var bindGroup in bindGroups)
            {
                bindGroup.Dispose();
            }

            _graphicsPipelineCache[hash] = pipelineNew;

            return pipelineNew;
        }
    }


    private GPUPipeline GetComputePipeline(ShaderModulesInfo modulesInfo)
    {
        if (_computePipelineCache.TryGetValue(modulesInfo, out GPUPipeline? pipeline))
        {
            return pipeline;
        }

        using (_lockCreateComputePipeline.EnterScope())
        {
            if (_computePipelineCache.TryGetValue(modulesInfo, out GPUPipeline? pipeline2))
            {
                return pipeline2;
            }

            if (!modulesInfo.IsComputeShader)
            {
                throw new InvalidOperationException("Trying to create a compute pipeline from a non-compute shader modules.");
            }

            ShaderReflectionInfo reflectionInfo = modulesInfo.ReflectionInfo;
            GPUDevice device = _renderingSystem.GraphicsDevice;

            GPUBindGroup[] bindGroups = new GPUBindGroup[reflectionInfo.BindGroups.Count];
            for (int i = 0; i < reflectionInfo.BindGroups.Count; i++)
            {
                bindGroups[i] = device.CreateBindGroup(reflectionInfo.BindGroups[i].ToDescriptor());
            }

            ComputePipelineDescriptor descriptor = new ComputePipelineDescriptor(
                modulesInfo.ComputeShader!.Value,
                bindGroups,
                Name);

            GPUPipeline pipelineNew = device.CreateComputePipeline(descriptor);

            foreach (var bindGroup in bindGroups)
            {
                bindGroup.Dispose();
            }

            _computePipelineCache[modulesInfo] = pipelineNew;
            return pipelineNew;
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var pipeline in _graphicsPipelineCache.Values)
            {
                pipeline.Dispose();
            }
            _graphicsPipelineCache.Clear();
            _modulesCache.Clear();
        }
    }
}