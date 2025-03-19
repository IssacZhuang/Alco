using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using System.Text.RegularExpressions;

namespace Alco.Rendering;

/// <summary>
/// The high level encapsulation of GPU pipeline
/// </summary>
public sealed class Shader : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly ConcurrentDictionary<int, ShaderModulesInfo> _modulesCache = new ConcurrentDictionary<int, ShaderModulesInfo>();
    private readonly ConcurrentDictionary<long, GPUPipeline> _graphicsPipelineCache = new ConcurrentDictionary<long, GPUPipeline>();
    private readonly ConcurrentDictionary<ShaderModulesInfo, GPUPipeline> _computePipelineCache = new ConcurrentDictionary<ShaderModulesInfo, GPUPipeline>();

    private readonly Lock _lockCreateGraphicsPipeline = new Lock();
    private readonly Lock _lockCreateComputePipeline = new Lock();
    private readonly Lock _lockCreateModules = new Lock();

    private readonly VertexInputLayout[]? _customVertexLayouts;

    private string _shaderText;
    //for hot reload
    private uint _version = 0;

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
    internal Shader(RenderingSystem renderingSystem, string shaderText, string name, VertexInputLayout[]? customVertexLayouts = null)
    {
        _renderingSystem = renderingSystem;
        _shaderText = shaderText;
        Name = name;

        //default permutation
        int hash = GetDefinesHash(ReadOnlySpan<string>.Empty);
        _modulesCache[hash] = UtilsShaderHLSL.Compile(shaderText, name, ReadOnlySpan<string>.Empty);

        _customVertexLayouts = customVertexLayouts;
    }

    /// <summary>
    /// Gets a graphics pipeline with the specified parameters and shader defines
    /// </summary>
    /// <param name="renderPass">The render pass configuration</param>
    /// <param name="depthStencil">The depth stencil state</param>
    /// <param name="blend">The blend state</param>
    /// <param name="rasterizer">The rasterizer state</param>
    /// <param name="primitiveTopology">The primitive topology</param>
    /// <param name="defines">Optional shader defines to customize compilation</param>
    /// <returns>A graphics pipeline context containing the configured pipeline and reflection info</returns>
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

    /// <summary>
    /// Gets a graphics pipeline with default rasterizer state and triangle list topology
    /// </summary>
    /// <param name="renderPass">The render pass configuration</param>
    /// <param name="depthStencil">The depth stencil state</param>
    /// <param name="blend">The blend state</param>
    /// <param name="defines">Optional shader defines to customize compilation</param>
    /// <returns>A graphics pipeline context containing the configured pipeline and reflection info</returns>
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

    /// <summary>
    /// Gets a graphics pipeline with default states for depth, blend, rasterizer and topology
    /// </summary>
    /// <param name="renderPass">The render pass configuration</param>
    /// <param name="defines">Optional shader defines to customize compilation</param>
    /// <returns>A graphics pipeline context containing the configured pipeline and reflection info</returns>
    public GraphicsPipelineContext GetGraphicsPipeline(
        GPURenderPass renderPass,
        params ReadOnlySpan<string> defines
        )
    {
        return GetGraphicsPipeline(
            renderPass,
            DepthStencilState.Read,
            BlendState.Opaque,
            RasterizerState.CullNone,
            PrimitiveTopology.TriangleList,
            defines
            );
    }

    /// <summary>
    /// Attempts to update an existing pipeline context with a new render pass
    /// </summary>
    /// <param name="pipelineInfo">The pipeline context to update</param>
    /// <param name="renderPass">The new render pass configuration</param>
    /// <param name="forced">Whether to force update even if render pass hasn't changed</param>
    /// <returns>True if the pipeline was updated, false otherwise</returns>
    public bool TryUpdatePipelineContext(ref GraphicsPipelineContext pipelineInfo, GPURenderPass renderPass, bool forced = false)
    {
        if (pipelineInfo.RenderPass == renderPass && !forced && pipelineInfo.Version == _version)
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
        pipelineInfo.Version = _version;

        return true;
    }


    public bool TryUpdateComputePipelineContext(ref ComputePipelineContext pipelineInfo, bool forced = false)
    {
        if (pipelineInfo.Version == _version && !forced)
        {
            return false;
        }

        ShaderModulesInfo modulesInfo = GetShaderModules(pipelineInfo.Defines);
        GPUPipeline pipeline = GetComputePipeline(modulesInfo);
        pipelineInfo.Pipeline = pipeline;
        pipelineInfo.ReflectionInfo = modulesInfo.ReflectionInfo;
        pipelineInfo.Version = _version;
        return true;
    }

    /// <summary>
    /// Gets a compute pipeline with the specified shader defines
    /// </summary>
    /// <param name="defines">Optional shader defines to customize compilation</param>
    /// <returns>A compute pipeline context containing the configured pipeline and reflection info</returns>
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

    /// <summary>
    /// Gets the compiled shader modules for the specified defines
    /// </summary>
    /// <param name="defines">Optional shader defines to customize compilation</param>
    /// <returns>The compiled shader modules information</returns>
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

    /// <summary>
    /// Precompiles the shader with the specified defines
    /// </summary>
    /// <param name="defines">Optional shader defines to customize compilation</param>
    public void Precompile(params ReadOnlySpan<string> defines)
    {
        int hash = GetDefinesHash(defines);
        _modulesCache[hash] = UtilsShaderHLSL.Compile(_shaderText, Name, defines);
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

    private unsafe GPUPipeline GetGraphicsPipeline(
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
        int hash1= renderPass.GetHashCode();

        //next 32 bits are combination of the variant hash and the pipeline state hash
        int hash2 = HashCode.Combine(
            modulesInfo.GetHashCode(),
            depthStencil.GetHashCode(),
            blend.GetHashCode(),
            rasterizer.GetHashCode(),
            primitiveTopology.GetHashCode()
            );

        int* hashPtr = (int*)&hash;
        hashPtr[0] = hash1;
        hashPtr[1] = hash2;

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

            VertexInputLayout[] vertexInputLayouts = _customVertexLayouts ?? reflectionInfo.VertexLayouts.ToArray();

            GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
                bindGroups,
                new ShaderModule[] {
                    modulesInfo.VertexShader!.Value,
                    modulesInfo.FragmentShader!.Value
                    },
                vertexInputLayouts,
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

    /// <summary>
    /// Unsafe hot reload the shader. It might break the material that uses this shader.
    /// So make sure the new shader has the same shader resource at the same slot.
    /// </summary>
    /// <param name="shaderText">The new shader text</param>
    public void UnsafeHotReload(string shaderText)
    {
        //it might throw exception if the shader code is not valid
        ShaderModulesInfo shaderModule = UtilsShaderHLSL.Compile(shaderText, Name, ReadOnlySpan<string>.Empty);
        
        _shaderText = shaderText;

        //clear cache
        _graphicsPipelineCache.Clear();
        _computePipelineCache.Clear();
        _modulesCache.Clear();

        //recompile
        int hash = GetDefinesHash(ReadOnlySpan<string>.Empty);
        _modulesCache[hash] = shaderModule;
        Interlocked.Increment(ref _version);
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

    //visible in Alco.Engine.Test
    internal void TestAllDefines(Action<string, string[], Exception> onError, Action<string, string[]> onSuccess)
    {
        //get defines from the shader text
        //like #if defined(TEST)
        var defineRegex = new Regex(@"#if\s+defined\s*\(\s*(\w+)\s*\)", RegexOptions.Compiled);
        var matches = defineRegex.Matches(_shaderText);

        // Extract unique define names
        HashSet<string> defines = new HashSet<string>();
        foreach (Match match in matches)
        {
            defines.Add(match.Groups[1].Value);
        }

        if (defines.Count == 0)
        {
            return; // No defines to test
        }

        // Convert defines to array for permutations
        string[] definesArray = defines.ToArray();

        // Test empty combination first
        try
        {
            GetShaderModules(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            onError(Name, Array.Empty<string>(), ex);
        }

        //default render pass 
        GPURenderPass renderPass = _renderingSystem.PrefferedHDRPass;

        // Generate and test all non-empty combinations
        for (int length = 1; length <= definesArray.Length; length++)
        {
            // Create array of first 'length' defines to generate permutations
            string[] subset = new string[length];
            Array.Copy(definesArray, subset, length);

            // Get all permutations of the current subset
            string[][] combinations = UtilsCollection.GetCombinations(subset);

            // Test each permutation
            foreach (string[] combination in combinations)
            {
                try
                {
                    var modulesInfo = GetShaderModules(combination);
                    if (modulesInfo.IsGraphicsShader)
                    {
                        var pipeline = GetGraphicsPipeline(
                        renderPass,
                            DepthStencilState.Default,
                            BlendState.Opaque,
                            RasterizerState.CullNone,
                            PrimitiveTopology.TriangleList,
                            combination);
                    }
                    else
                    {
                        var pipeline = GetComputePipeline(modulesInfo);
                    }



                }
                catch (Exception ex)
                {
                    onError(Name, combination, ex);
                }
                onSuccess(Name, combination);
            }
        }
    }

}