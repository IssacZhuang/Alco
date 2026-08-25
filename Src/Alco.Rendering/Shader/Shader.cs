using System.Collections.Concurrent;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The high level encapsulation of GPU pipeline
/// </summary>
public sealed class Shader : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;

    // A Shader is one module's handle (plan §4.4/D3): its entry points are the
    // module's own [shader(...)] definitions and every variant axis is a generic
    // value specialization, requested where the retired defines used to be —
    // through the specialization arguments of the accessor methods below. The
    // module compiles lazily, once per specialization, and the compiled modules
    // are cached inside this object.
    private readonly Func<string[], ShaderModulesInfo> _compileModules;
    // Thread safety: ConcurrentDictionary keeps the lock-free read path safe
    // while another thread compiles a new specialization (materials may be
    // created on any number of threads); the create lock below keeps one
    // compile per key.
    private readonly ConcurrentDictionary<string, ShaderModulesInfo> _modulesInfos = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<long, GPUPipeline> _graphicsPipelineCache = new ConcurrentDictionary<long, GPUPipeline>();
    private readonly ConcurrentDictionary<ShaderModulesInfo, GPUPipeline> _computePipelineCache = new ConcurrentDictionary<ShaderModulesInfo, GPUPipeline>();

    private readonly Lock _lockCreateGraphicsPipeline = new Lock();
    private readonly Lock _lockCreateComputePipeline = new Lock();
    private readonly Lock _lockCreateModules = new Lock();

    private readonly IReadOnlyList<VertexInputLayout>? _customVertexLayouts;

    //for hot reload
    private uint _version = 0;

    /// <summary>
    /// The name of the shader
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Create a new shader handle whose modules are compiled through the slang
    /// module system: the compiler is called once per specialization, on demand.
    /// Nothing compiles at construction — a module with generic entry points
    /// (e.g. <c>MainPS&lt;let Quality : int&gt;</c>) links only when one of its
    /// specializations is first requested.
    /// </summary>
    /// <param name="renderingSystem">The rendering system</param>
    /// <param name="name">The name of the shader</param>
    /// <param name="compileModules">Produces the compiled modules of one specialization.</param>
    /// <param name="customVertexLayouts">Optional vertex layout override (e.g. ImGui's packed Unorm8x4 color).</param>
    internal Shader(RenderingSystem renderingSystem, string name, Func<string[], ShaderModulesInfo> compileModules,
        IReadOnlyList<VertexInputLayout>? customVertexLayouts = null)
    {
        _renderingSystem = renderingSystem;
        Name = name;
        _compileModules = compileModules;
        _customVertexLayouts = customVertexLayouts;
    }

    /// <summary>
    /// Gets the compiled shader modules of one specialization (cached per
    /// specialization; the default, empty arguments link the module unspecialized).
    /// </summary>
    /// <param name="specializations">The specialization arguments — C# values
    /// (<c>false</c>, <c>3</c>) or slang expressions (type names) mapped to the
    /// entry points' generic parameters in definition order; they are normalized
    /// to the canonical slang literal strings internally.</param>
    public ShaderModulesInfo GetShaderModules(params ReadOnlySpan<object> specializations)
        => GetShaderModules(NormalizeSpecializations(specializations));

    /// <summary>
    /// Canonical-string core the internal pipeline uses: materials and pipeline
    /// contexts already hold normalized specialization strings.
    /// </summary>
    internal ShaderModulesInfo GetShaderModules(string[] specializations)
    {
        string key = SpecializationKey(specializations);
        if (_modulesInfos.TryGetValue(key, out ShaderModulesInfo? cached))
        {
            return cached;
        }

        using (_lockCreateModules.EnterScope())
        {
            if (_modulesInfos.TryGetValue(key, out ShaderModulesInfo? cached2))
            {
                return cached2;
            }
            // The module system owns its own disk caches (module IR + linked
            // programs); the shader keeps only the in-memory modules reference.
            ShaderModulesInfo modulesInfo = _compileModules(specializations);
            _modulesInfos[key] = modulesInfo;
            return modulesInfo;
        }
    }

    /// <summary>
    /// Gets a graphics pipeline with the specified parameters
    /// </summary>
    /// <param name="attachmentLayout">The attachment layout configuration</param>
    /// <param name="depthStencil">The depth stencil state</param>
    /// <param name="blend">The blend state</param>
    /// <param name="rasterizer">The rasterizer state</param>
    /// <param name="primitiveTopology">The primitive topology</param>
    /// <param name="specializations">The specialization arguments of the variant to build.</param>
    /// <returns>A graphics pipeline context containing the configured pipeline and reflection info</returns>
    public GraphicsPipelineContext GetGraphicsPipeline(
        GPUAttachmentLayout attachmentLayout,
        DepthStencilState depthStencil,
        BlendState blend,
        RasterizerState rasterizer,
        PrimitiveTopology primitiveTopology,
        params ReadOnlySpan<object> specializations
        )
    {
        string[] spec = NormalizeSpecializations(specializations);
        ShaderModulesInfo modulesInfo = GetShaderModules(spec);
        GPUPipeline pipeline = GetGraphicsPipeline(attachmentLayout, modulesInfo, depthStencil, blend, rasterizer, primitiveTopology);
        return new GraphicsPipelineContext
        {
            Pipeline = pipeline,
            AttachmentLayout = attachmentLayout,
            ReflectionInfo = modulesInfo.ReflectionInfo,
            DepthStencil = depthStencil,
            BlendState = blend,
            Rasterizer = rasterizer,
            PrimitiveTopology = primitiveTopology,
            Specializations = spec,
        };
    }

    /// <summary>
    /// Gets a graphics pipeline with default rasterizer state and triangle list topology
    /// </summary>
    public GraphicsPipelineContext GetGraphicsPipeline(
        GPUAttachmentLayout attachmentLayout,
        DepthStencilState depthStencil,
        BlendState blend,
        params ReadOnlySpan<object> specializations
        )
    {
        return GetGraphicsPipeline(
            attachmentLayout,
            depthStencil,
            blend,
            RasterizerState.CullNone,
            PrimitiveTopology.TriangleList,
            specializations
            );
    }

    /// <summary>
    /// Gets a graphics pipeline with default states for depth, blend, rasterizer and topology
    /// </summary>
    public GraphicsPipelineContext GetGraphicsPipeline(
        GPUAttachmentLayout attachmentLayout,
        params ReadOnlySpan<object> specializations
        )
    {
        return GetGraphicsPipeline(
            attachmentLayout,
            DepthStencilState.Read,
            BlendState.Opaque,
            RasterizerState.CullNone,
            PrimitiveTopology.TriangleList,
            specializations
            );
    }

    /// <summary>
    /// Attempts to update an existing pipeline context with a new attachment layout.
    /// The context keeps the specialization it was built for (set by the
    /// <see cref="GetGraphicsPipeline(GPUAttachmentLayout, DepthStencilState, BlendState, RasterizerState, PrimitiveTopology, ReadOnlySpan{object})"/>
    /// call that created it) — switching variants means building a fresh context
    /// with different specialization arguments.
    /// </summary>
    /// <param name="pipelineInfo">The pipeline context to update</param>
    /// <param name="attachmentLayout">The new attachment layout configuration</param>
    /// <param name="forced">Whether to force update even if attachment layout hasn't changed</param>
    /// <returns>True if the pipeline was updated, false otherwise</returns>
    public bool TryUpdatePipelineContext(ref GraphicsPipelineContext pipelineInfo, GPUAttachmentLayout attachmentLayout, bool forced = false)
    {
        if (pipelineInfo.AttachmentLayout == attachmentLayout && !forced && pipelineInfo.Version == _version)
        {
            return false;
        }

        ShaderModulesInfo modulesInfo = GetShaderModules(pipelineInfo.Specializations ?? []);

        GPUPipeline pipeline = GetGraphicsPipeline(
            attachmentLayout,
            modulesInfo,
            pipelineInfo.DepthStencil,
            pipelineInfo.BlendState,
            pipelineInfo.Rasterizer,
            pipelineInfo.PrimitiveTopology
            );

        pipelineInfo.Pipeline = pipeline;
        pipelineInfo.AttachmentLayout = attachmentLayout;
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

        ShaderModulesInfo modulesInfo = GetShaderModules(pipelineInfo.Specializations ?? []);
        GPUPipeline pipeline = GetComputePipeline(modulesInfo);
        pipelineInfo.Pipeline = pipeline;
        pipelineInfo.ReflectionInfo = modulesInfo.ReflectionInfo;
        pipelineInfo.Version = _version;
        return true;
    }

    /// <summary>
    /// Gets a compute pipeline context for one specialization.
    /// </summary>
    /// <param name="specializations">The specialization arguments of the variant to build.</param>
    /// <returns>A compute pipeline context containing the configured pipeline and reflection info</returns>
    public ComputePipelineContext GetComputePipelineInfo(params ReadOnlySpan<object> specializations)
    {
        string[] spec = NormalizeSpecializations(specializations);
        ShaderModulesInfo modulesInfo = GetShaderModules(spec);
        GPUPipeline pipeline = GetComputePipeline(modulesInfo);
        return new ComputePipelineContext
        {
            Pipeline = pipeline,
            ReflectionInfo = modulesInfo.ReflectionInfo,
            Specializations = spec,
        };
    }

    private static string SpecializationKey(string[] specializations)
        => string.Join("|", specializations);

    /// <summary>
    /// Normalizes object-typed specialization arguments into the canonical slang
    /// expression strings used as cache keys and handed to the compiler: bool
    /// takes the lowercase slang literals (C# <c>ToString()</c> casing is
    /// rejected by slang's expression parser), the integer types invariant
    /// digits, strings pass through (identifiers, type names). Slang value axes
    /// are integer/enum only (error E30624), so floating-point arguments are
    /// rejected here with a clear message instead of failing inside the compiler.
    /// </summary>
    internal static string[] NormalizeSpecializations(ReadOnlySpan<object> specializations)
    {
        if (specializations.IsEmpty)
        {
            return [];
        }

        string[] normalized = new string[specializations.Length];
        for (int i = 0; i < specializations.Length; i++)
        {
            normalized[i] = specializations[i] switch
            {
                bool b => b ? "true" : "false",
                int v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                uint v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                long v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ulong v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                short v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ushort v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                sbyte v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                byte v => v.ToString(System.Globalization.CultureInfo.InvariantCulture),
                string s => s,
                null => throw new ArgumentException(
                    "Specialization arguments cannot be null; pass bool, an integer type or a string.", nameof(specializations)),
                _ => throw new ArgumentException(
                    $"Unsupported specialization argument type '{specializations[i].GetType().Name}': " +
                    "pass bool, an integer type or a string — slang value axes are integer/enum only " +
                    "(E30624), so floating-point values cannot be specialized.", nameof(specializations)),
            };
        }
        return normalized;
    }

    private unsafe GPUPipeline GetGraphicsPipeline(
        GPUAttachmentLayout attachmentLayout,
        ShaderModulesInfo modulesInfo,
        DepthStencilState depthStencil,
        BlendState blend,
        RasterizerState rasterizer,
        PrimitiveTopology primitiveTopology
        )
    {

        long hash = default;
        //fist 32 bits are the attachment layout hash
        int hash1= attachmentLayout.GetHashCode();

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

            IReadOnlyList<BindGroupLayout> bindGroupLayouts = reflectionInfo.BindGroups;

            GPUBindGroup[] bindGroups = new GPUBindGroup[bindGroupLayouts.Count];
            for (int i = 0; i < bindGroupLayouts.Count; i++)
            {
                bindGroups[i] = device.CreateBindGroup(bindGroupLayouts[i].ToDescriptor());
            }

            GPUPipeline pipelineNew;


            PixelFormat[] colors = new PixelFormat[attachmentLayout.Colors.Length];
            for (int i = 0; i < attachmentLayout.Colors.Length; i++)
            {
                colors[i] = attachmentLayout.Colors[i].Format;
            }
            PixelFormat? depthStencilFormat = attachmentLayout.Depth?.Format;

            IReadOnlyList<VertexInputLayout> vertexInputLayouts = _customVertexLayouts ?? reflectionInfo.VertexLayouts;

            GraphicsPipelineDescriptor descriptor = new GraphicsPipelineDescriptor(
                bindGroups,
                new ShaderModule[] {
                    modulesInfo.VertexShader!.Value,
                    modulesInfo.FragmentShader!.Value
                    },
                vertexInputLayouts.ToArray(),
                rasterizer,
                blend,
                depthStencil,
                primitiveTopology,
                colors,
                depthStencilFormat,
                (uint)reflectionInfo.PushConstantsSize,
                Name)
            {
                FragmentOutputCount = reflectionInfo.FragmentOutputCount,
            };


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
                (uint)reflectionInfo.PushConstantsSize,
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
    /// Create a new material that uses this shader
    /// </summary>
    /// <returns>The new material</returns>
    public GraphicsMaterial CreateGraphicsMaterial(string name = "unamed_material")
    {
        return _renderingSystem.CreateGraphicsMaterial(this, name);
    }

    /// <summary>
    /// Module-based hot reload (plan Phase 1): the ShaderSystem invalidated this shader's
    /// module; drop the compiled modules of every specialization and every cached
    /// pipeline so the next use recompiles from the module system's current sources.
    /// The version bump drives lazy pipeline rebuilds through the existing
    /// TryUpdatePipelineContext mechanism.
    /// </summary>
    internal void UnsafeModuleReload()
    {
        _graphicsPipelineCache.Clear();
        _computePipelineCache.Clear();
        _modulesInfos.Clear();
        Interlocked.Increment(ref _version);
    }

    /// <summary>Monotonic version bumped on every reload; consumers use it to rebuild pipelines lazily.</summary>
    public uint Version => _version;


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var pipeline in _graphicsPipelineCache.Values)
            {
                pipeline.Dispose();
            }
            _graphicsPipelineCache.Clear();
            _modulesInfos.Clear();
        }
    }
}
