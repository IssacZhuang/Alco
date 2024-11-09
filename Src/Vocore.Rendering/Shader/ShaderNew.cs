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
    private readonly ConcurrentDictionary<int, ShaderModulesInfo> _modulesCache = new ConcurrentDictionary<int, ShaderModulesInfo>();
    private readonly ConcurrentDictionary<long, GPUPipeline> _pipelineCache = new ConcurrentDictionary<long, GPUPipeline>();

    private readonly Lock _lockCreatePipeline = new Lock();
    private readonly Lock _lockCreateModules = new Lock();
    private readonly string _shaderText;

    public string Name { get; }

    internal ShaderNew(RenderingSystem renderingSystem, string shaderText, string name)
    {
        _renderingSystem = renderingSystem;
        _shaderText = shaderText;
        Name = name;

        //default permutation
        int hash = GetDefinesHash(ReadOnlySpan<string>.Empty);
        _modulesCache[hash] = UtilsShaderHLSL.Compile(shaderText, name, ReadOnlySpan<string>.Empty);
    }

    public ShaderPipelineInfo GetGraphicsPipeline(
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
        return new ShaderPipelineInfo
        {
            Pipeline = pipeline,
            RenderPass = renderPass,
            ModulesInfo = modulesInfo,
            ReflectionInfo = modulesInfo.ReflectionInfo,
            DepthStencil = depthStencil,
            BlendState = blend,
            Rasterizer = rasterizer,
            PrimitiveTopology = primitiveTopology
        };
    }

    public ShaderPipelineInfo GetGraphicsPipeline(
        GPURenderPass renderPass,
        params ReadOnlySpan<string> defines
        )
    {
        return GetGraphicsPipeline(
            renderPass,
            DepthStencilState.None,
            BlendState.Opaque,
            RasterizerState.CullNone,
            PrimitiveTopology.TriangleList,
            defines
            );
    }

    public bool TryUpdatePipelineInfo(ref ShaderPipelineInfo pipelineInfo, GPURenderPass renderPass)
    {
        if (pipelineInfo.RenderPass == renderPass)
        {
            return false;
        }

        GPUPipeline pipeline = GetGraphicsPipeline(
            renderPass,
            pipelineInfo.ModulesInfo,
            pipelineInfo.DepthStencil,
            pipelineInfo.BlendState,
            pipelineInfo.Rasterizer,
            pipelineInfo.PrimitiveTopology
            );

        pipelineInfo.Pipeline = pipeline;
        pipelineInfo.RenderPass = renderPass;

        return true;
    }


    private ShaderModulesInfo GetShaderModules(params ReadOnlySpan<string> defines)
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

            _pipelineCache[hash] = pipelineNew;

            return pipelineNew;
        }
    }



    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var pipeline in _pipelineCache.Values)
            {
                pipeline.Dispose();
            }
            _pipelineCache.Clear();
            _modulesCache.Clear();
        }
    }
}