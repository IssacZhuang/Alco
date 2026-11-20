// Example-level shader handle: automatic stage inference, template/interface composition, lazy specialization variants, and shared pipelines.

using System.Diagnostics;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>The pipeline kind of a shader's selected entry points.</summary>
public enum ShaderKind
{
    /// <summary>Vertex and optional pixel stages.</summary>
    Graphics,

    /// <summary>One compute stage.</summary>
    Compute,

    /// <summary>Mesh with optional task and pixel stages.</summary>
    Mesh,
}

/// <summary>The bindless kind of one root field, derived from its reflected type.</summary>
public enum MaterialFieldKind
{
    /// <summary>An ordinary data field written through <see cref="Material.SetData{T}"/>.</summary>
    Data,

    /// <summary>A shader <c>GPUBufferView</c> field written through <see cref="Material.SetBuffer"/>.</summary>
    Buffer,

    /// <summary>A texture descriptor index field (<c>GpuTextureId</c>).</summary>
    Texture,

    /// <summary>A sampler descriptor index field (<c>GpuSamplerId</c>).</summary>
    Sampler,
}

/// <summary>One reflected root field with its uniform offset and size.</summary>
public sealed class RootField
{
    /// <summary>The field's declared name.</summary>
    public required string Name { get; init; }

    /// <summary>The byte offset within the root struct.</summary>
    public required uint Offset { get; init; }

    /// <summary>The field size in bytes.</summary>
    public required uint SizeBytes { get; init; }

    /// <summary>The bindless field kind.</summary>
    public required MaterialFieldKind Kind { get; init; }
}

/// <summary>The reflected layout of one shader's GPU_ROOT struct.</summary>
public sealed class RootLayout
{
    private readonly RootField[] _fields;
    private readonly RootField[] _leafFields;
    private readonly ShaderRootLayout? _sourceLayout;

    /// <summary>Creates a layout over the reflected fields.</summary>
    /// <param name="sizeBytes">The root struct size in bytes.</param>
    /// <param name="fields">The fields in declaration order.</param>
    public RootLayout(uint sizeBytes, RootField[] fields)
    {
        SizeBytes = sizeBytes;
        _fields = fields;
        _leafFields = fields;
    }

    internal RootLayout(ShaderRootLayout source)
    {
        _sourceLayout = source;
        SizeBytes = source.SizeBytes;
        _fields = new RootField[source.Fields.Count];
        for (int index = 0; index < _fields.Length; index++)
            _fields[index] = ToRootField(source.Fields[index]);
        List<RootField> leaves = new(source.Fields.Count);
        CollectLeafFields(source.Fields, prefix: null, baseOffset: 0, leaves);
        _leafFields = leaves.ToArray();
    }

    /// <summary>The root struct size in bytes.</summary>
    public uint SizeBytes { get; }

    /// <summary>The top-level fields in declaration order.</summary>
    public ReadOnlySpan<RootField> Fields => _fields;

    /// <summary>All leaf fields (flattening nested structs with dotted names and cumulative offsets).</summary>
    internal ReadOnlySpan<RootField> LeafFields => _leafFields;

    /// <summary>Finds a field by name or dotted nested path.</summary>
    /// <param name="name">The field name or dot-separated nested path.</param>
    /// <returns>The field, or null when the root declares no such name.</returns>
    public RootField? Find(string name)
    {
        foreach (RootField field in _fields)
        {
            if (field.Name == name)
                return field;
        }
        if (_sourceLayout != null && name.Contains('.'))
        {
            foreach (RootField leaf in _leafFields)
            {
                if (leaf.Name == name)
                    return leaf;
            }
            ShaderFieldLayout? nested = _sourceLayout.Find(name);
            if (nested != null)
                return ToRootField(nested);
        }
        return null;
    }

    internal RootField? FindCompatibleLeaf(RootField field)
    {
        foreach (RootField candidate in _leafFields)
        {
            if (candidate.Name == field.Name && candidate.Kind == field.Kind && candidate.SizeBytes == field.SizeBytes)
                return candidate;
        }
        return null;
    }

    internal bool MatchesLayout(RootLayout other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (SizeBytes != other.SizeBytes || _leafFields.Length != other._leafFields.Length)
            return false;
        for (int index = 0; index < _leafFields.Length; index++)
        {
            RootField left = _leafFields[index];
            RootField right = other._leafFields[index];
            if (left.Offset != right.Offset || left.SizeBytes != right.SizeBytes || left.Kind != right.Kind || left.Name != right.Name)
                return false;
        }
        return true;
    }

    private static void CollectLeafFields(IReadOnlyList<ShaderFieldLayout> fields, string? prefix, uint baseOffset, List<RootField> destination)
    {
        for (int index = 0; index < fields.Count; index++)
        {
            ShaderFieldLayout field = fields[index];
            string fullName = prefix == null ? field.Name : $"{prefix}.{field.Name}";
            uint offset = baseOffset + field.OffsetBytes;
            if (field.Semantic == ShaderFieldSemantic.Data && field.Type.Kind == ShaderTypeKind.Struct && field.Type.Fields.Count > 0)
            {
                CollectLeafFields(field.Type.Fields, fullName, offset, destination);
            }
            else
            {
                destination.Add(new RootField
                {
                    Name = fullName,
                    Offset = offset,
                    SizeBytes = field.SizeBytes,
                    Kind = ToFieldKind(field.Semantic),
                });
            }
        }
    }

    private static RootField ToRootField(ShaderFieldLayout field) => new()
    {
        Name = field.Name,
        Offset = field.OffsetBytes,
        SizeBytes = field.SizeBytes,
        Kind = ToFieldKind(field.Semantic),
    };

    private static MaterialFieldKind ToFieldKind(ShaderFieldSemantic semantic) => semantic switch
    {
        ShaderFieldSemantic.BufferView => MaterialFieldKind.Buffer,
        ShaderFieldSemantic.TextureId => MaterialFieldKind.Texture,
        ShaderFieldSemantic.SamplerId => MaterialFieldKind.Sampler,
        _ => MaterialFieldKind.Data,
    };
}

/// <summary>The pipeline-defining state of a material: per-target blend, write mask, rasterization, and topology.</summary>
public struct PipelineState
{
    /// <summary>The blend state applied to every color target.</summary>
    public BlendState Blend;

    /// <summary>The color write mask applied to every color target.</summary>
    public byte WriteMask;

    /// <summary>The rasterizer configuration.</summary>
    public RasterizationState Rasterization;

    /// <summary>The vertex assembly topology.</summary>
    public PrimitiveTopology Topology;

    /// <summary>Initializes a triangle-list state with disabled blending and all color channels writable.</summary>
    public PipelineState()
    {
        Blend = new BlendState();
        WriteMask = 0xf;
        Rasterization = new RasterizationState();
        Topology = PrimitiveTopology.TriangleList;
    }

    internal readonly bool Matches(PipelineState state, ShaderKind kind)
    {
        return Blend.Enabled == state.Blend.Enabled &&
            Blend.Color.Source == state.Blend.Color.Source && Blend.Color.Destination == state.Blend.Color.Destination &&
            Blend.Color.Operation == state.Blend.Color.Operation &&
            Blend.Alpha.Source == state.Blend.Alpha.Source && Blend.Alpha.Destination == state.Blend.Alpha.Destination &&
            Blend.Alpha.Operation == state.Blend.Alpha.Operation && WriteMask == state.WriteMask &&
            (kind != ShaderKind.Graphics || Topology == state.Topology) &&
            Rasterization.Cull == state.Rasterization.Cull &&
            Rasterization.DepthBiasConstant.Equals(state.Rasterization.DepthBiasConstant) &&
            Rasterization.DepthBiasClamp.Equals(state.Rasterization.DepthBiasClamp) &&
            Rasterization.DepthBiasSlope.Equals(state.Rasterization.DepthBiasSlope);
    }
}

/// <summary>One compiled specialization variant of a <see cref="Shader"/>, owning its reflected root layout and GPU pipelines.</summary>
internal sealed class ShaderVariant
{
    private readonly Shader _owner;
    private readonly CompiledShaderStage? _primary;
    private readonly CompiledShaderStage? _task;
    private readonly CompiledShaderStage? _pixel;
    private readonly GPUPipeline? _computePipeline;
    private readonly Lock _pipelineLock = new();
    private PipelineEntry[] _pipelines = [];

    internal ShaderVariant(Shader owner, CompiledShader compiled, ShaderKind kind, string[] specializations)
    {
        _owner = owner;
        Compiled = compiled;
        Specializations = Array.AsReadOnly(specializations);
        ShaderRootLayout root = compiled.Reflection.Root
            ?? throw new InvalidOperationException($"Shader '{owner.Name}' declares no GPU_ROOT root struct.");
        Root = new RootLayout(root);

        if (kind == ShaderKind.Compute)
        {
            CompiledShaderStage compute = compiled.GetStage(ShaderStageKind.Compute);
            _computePipeline = owner.System.Device.CreateComputePipeline(compute.ToGpuStage());
        }
        else if (kind == ShaderKind.Mesh)
        {
            _primary = compiled.GetStage(ShaderStageKind.Mesh);
            _task = FindUniqueOptionalStage(compiled, ShaderStageKind.Task);
            _pixel = FindUniqueOptionalStage(compiled, ShaderStageKind.Pixel);
        }
        else if (kind == ShaderKind.Graphics)
        {
            _primary = compiled.GetStage(ShaderStageKind.Vertex);
            _pixel = FindUniqueOptionalStage(compiled, ShaderStageKind.Pixel);
        }
        else
            throw new ArgumentOutOfRangeException(nameof(kind));
    }

    internal CompiledShader Compiled { get; }
    internal RootLayout Root { get; }
    internal IReadOnlyList<string> Specializations { get; }

    internal GPUPipeline GetComputePipeline()
    {
        return _computePipeline ?? throw new InvalidOperationException($"Shader '{_owner.Name}' is not a compute shader.");
    }

    internal GPUPipeline GetPipeline(in PipelineState state, ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat)
    {
        if (_owner.Kind == ShaderKind.Compute)
            throw new InvalidOperationException($"Compute shader '{_owner.Name}' has no raster pipeline.");
        GPUPipeline? cached = FindPipeline(Volatile.Read(ref _pipelines), state, colorFormats, depthFormat, stencilFormat);
        if (cached != null)
            return cached;

        lock (_pipelineLock)
        {
            PipelineEntry[] pipelines = _pipelines;
            cached = FindPipeline(pipelines, state, colorFormats, depthFormat, stencilFormat);
            if (cached != null)
                return cached;

            ColorTargetDesc[] targets = new ColorTargetDesc[colorFormats.Length];
            for (int index = 0; index < targets.Length; index++)
                targets[index] = new ColorTargetDesc { Format = colorFormats[index], Blend = state.Blend, WriteMask = state.WriteMask };
            PipelineEntry[] updated = new PipelineEntry[pipelines.Length + 1];
            pipelines.CopyTo(updated, 0);
            PipelineKey key = new(colorFormats, depthFormat, stencilFormat, state);
            GPUPipeline pipeline = _owner.Kind == ShaderKind.Mesh
                ? _owner.System.Device.CreateMeshPipeline(new MeshPipelineDesc
                {
                    Task = _task != null ? _task.ToGpuStage() : default,
                    Mesh = _primary!.ToGpuStage(),
                    Pixel = _pixel != null ? _pixel.ToGpuStage() : default,
                    ColorTargets = targets,
                    DepthFormat = depthFormat,
                    StencilFormat = stencilFormat,
                    Rasterization = state.Rasterization,
                })
                : _owner.System.Device.CreateGraphicsPipeline(new GraphicsPipelineDesc
                {
                    Vertex = _primary!.ToGpuStage(),
                    Pixel = _pixel != null ? _pixel.ToGpuStage() : default,
                    ColorTargets = targets,
                    DepthFormat = depthFormat,
                    StencilFormat = stencilFormat,
                    Rasterization = state.Rasterization,
                    Topology = state.Topology,
                });
            updated[^1] = new PipelineEntry { Key = key, Pipeline = pipeline };
            Volatile.Write(ref _pipelines, updated);
            return pipeline;
        }
    }

    internal GPUPipeline GetPreparedPipeline(PipelineState state, ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat)
    {
        return FindPipeline(Volatile.Read(ref _pipelines), state, colorFormats, depthFormat, stencilFormat) ??
            throw new InvalidOperationException($"Shader '{_owner.Name}' needs a prepared pipeline; call Material.GetPipeline for this state and pass.");
    }

    internal void Destroy()
    {
        _computePipeline?.Destroy();
        foreach (PipelineEntry entry in _pipelines)
            entry.Pipeline.Destroy();
        _pipelines = [];
    }

    private GPUPipeline? FindPipeline(PipelineEntry[] pipelines, PipelineState state, ReadOnlySpan<Format> colorFormats,
        Format depthFormat, Format stencilFormat)
    {
        foreach (PipelineEntry entry in pipelines)
        {
            if (entry.Key.Matches(colorFormats, depthFormat, stencilFormat, state, _owner.Kind))
                return entry.Pipeline;
        }
        return null;
    }

    private static CompiledShaderStage? FindUniqueOptionalStage(CompiledShader compiled, ShaderStageKind stage)
    {
        CompiledShaderStage? match = null;
        for (int index = 0; index < compiled.Stages.Count; ++index)
        {
            if (compiled.Stages[index].Stage != stage)
                continue;
            if (match != null)
                throw new InvalidOperationException($"Shader '{compiled.ModuleName}' has more than one {stage} stage.");
            match = compiled.Stages[index];
        }
        return match;
    }

    private struct PipelineEntry
    {
        public PipelineKey Key;
        public GPUPipeline Pipeline;
    }

    private readonly struct PipelineKey
    {
        public readonly Format[] ColorFormats;
        public readonly Format DepthFormat;
        public readonly Format StencilFormat;
        public readonly PipelineState State;

        public PipelineKey(ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat, in PipelineState state)
        {
            ColorFormats = colorFormats.ToArray();
            DepthFormat = depthFormat;
            StencilFormat = stencilFormat;
            State = state;
        }

        public bool Matches(ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat, in PipelineState state, ShaderKind kind)
        {
            return DepthFormat == depthFormat && StencilFormat == stencilFormat &&
                ColorFormats.AsSpan().SequenceEqual(colorFormats) && State.Matches(state, kind);
        }
    }
}

/// <summary>A multi-variant shader handle supporting automatic stage inference, template/interface composition, and lazy specialization.</summary>
public sealed class Shader : RenderingResource
{
    private readonly Func<string[], CompiledShader> _compileVariant;
    private readonly Lock _variantLock = new();
    private VariantEntry[] _variants = [];
    private ShaderVariant? _defaultVariant;

    /// <summary>Creates a shader handle for a module, automatically inferring Graphics, Compute, or Mesh when <paramref name="kind"/> is null.</summary>
    /// <param name="system">The rendering system that owns the device.</param>
    /// <param name="compiler">The shader compiler resolving the module; must stay alive while new specializations are compiled.</param>
    /// <param name="module">The module name in the compiler's search paths.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers automatically from the module's entry points.</param>
    /// <param name="name">The shader name; defaults to the module name.</param>
    public Shader(RenderingSystem system, ShaderCompiler compiler, string module, ShaderKind? kind = null, string name = "") : base(system)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(compiler);
            ArgumentException.ThrowIfNullOrEmpty(module);

            ShaderModule parsed = compiler.LoadModule(module);
            ShaderKind resolvedKind = kind ?? FromPipelineKind(parsed.InferPipelineKind());
            ShaderPipelineKind pipelineKind = ToPipelineKind(resolvedKind);

            Name = name.Length != 0 ? name : module;
            Kind = resolvedKind;
            _compileVariant = specs => compiler.Compile(module, pipelineKind,
                specs.Length > 0 ? new ShaderCompileOptions { SpecializationArgs = specs } : null);

            if (!HasGenericStages(parsed, resolvedKind))
                _defaultVariant = GetOrCompileVariant([]);
        }
        catch
        {
            GC.SuppressFinalize(this);
            throw;
        }
    }

    /// <summary>Creates a shader handle over a pre-loaded module, automatically inferring the pipeline kind when <paramref name="kind"/> is null.</summary>
    /// <param name="system">The rendering system that owns the device.</param>
    /// <param name="module">The parsed shader module.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers automatically from the module's entry points.</param>
    /// <param name="name">The shader name; defaults to the module name.</param>
    public Shader(RenderingSystem system, ShaderModule module, ShaderKind? kind = null, string name = "") : base(system)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(module);
            ShaderKind resolvedKind = kind ?? FromPipelineKind(module.InferPipelineKind());
            ShaderPipelineKind pipelineKind = ToPipelineKind(resolvedKind);

            Name = name.Length != 0 ? name : module.Name;
            Kind = resolvedKind;
            _compileVariant = specs => module.Compile(pipelineKind,
                specs.Length > 0 ? new ShaderCompileOptions { SpecializationArgs = specs } : null);

            if (!HasGenericStages(module, resolvedKind))
                _defaultVariant = GetOrCompileVariant([]);
        }
        catch
        {
            GC.SuppressFinalize(this);
            throw;
        }
    }

    private Shader(
        RenderingSystem system,
        string name,
        ShaderKind kind,
        Func<string[], CompiledShader> compileVariant) : base(system)
    {
        Name = name;
        Kind = kind;
        _compileVariant = compileVariant;
    }

    /// <summary>Creates a composed multi-variant shader pairing a template module with a surface module implementing its contract.</summary>
    /// <param name="system">The rendering system that owns the device.</param>
    /// <param name="compiler">The shader compiler; must stay alive while new value specializations are compiled.</param>
    /// <param name="templateModule">The pass template module declaring generic entry points.</param>
    /// <param name="surfaceModule">The companion module exporting one struct implementing the template's contract.</param>
    /// <param name="kind">Optional explicit pipeline kind; null infers automatically from the template module.</param>
    /// <param name="name">The shader name; defaults to <c>template+surface</c>.</param>
    /// <returns>The composed multi-variant shader handle.</returns>
    public static Shader Compose(
        RenderingSystem system,
        ShaderCompiler compiler,
        string templateModule,
        string surfaceModule,
        ShaderKind? kind = null,
        string name = "")
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentException.ThrowIfNullOrEmpty(templateModule);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModule);

        ShaderModule parsedTemplate = compiler.LoadModule(templateModule);
        ShaderKind resolvedKind = kind ?? FromPipelineKind(parsedTemplate.InferPipelineKind());
        ShaderPipelineKind pipelineKind = ToPipelineKind(resolvedKind);
        string resolvedName = name.Length != 0 ? name : $"{templateModule}+{surfaceModule}";
        return new Shader(
            system,
            resolvedName,
            resolvedKind,
            specs => compiler.CompileComposed(templateModule, surfaceModule, pipelineKind,
                specs.Length > 0 ? new ShaderCompileOptions { SpecializationArgs = specs } : null));
    }

    /// <summary>Normalizes C# specialization values into canonical Slang expression strings.</summary>
    /// <param name="specializations">Boolean, integer, enum, or Slang expression string arguments.</param>
    /// <returns>The normalized specialization strings.</returns>
    public static string[] NormalizeSpecializations(ReadOnlySpan<object> specializations) =>
        ShaderCompiler.NormalizeSpecializations(specializations);

    /// <summary>The shader name used in diagnostics.</summary>
    public string Name { get; }

    /// <summary>The pipeline kind of the selected program.</summary>
    public ShaderKind Kind { get; }

    /// <summary>The rendering system this shader creates pipelines on.</summary>
    public RenderingSystem System => _system;

    /// <summary>The reflected root struct layout of the default specialization variant.</summary>
    public RootLayout Root => DefaultVariant.Root;

    /// <summary>The full shader reflection snapshot of the default specialization variant.</summary>
    public ShaderReflection Reflection => DefaultVariant.Compiled.Reflection;

    internal ShaderVariant DefaultVariant => _defaultVariant ??= GetOrCompileVariant([]);

    /// <summary>Creates a material over this shader pinned to the given specialization arguments.</summary>
    /// <param name="name">The material name; defaults to the shader name.</param>
    /// <param name="specializations">Optional specialization arguments (bool, integer, enum, or Slang expression string).</param>
    /// <returns>The material; it keeps this shader and its bound resources alive and needs no destruction.</returns>
    public Material CreateMaterial(string name = "", params ReadOnlySpan<object> specializations)
    {
        Debug.Assert(!IsDestroyed);
        return new Material(this, name, GetOrCompileVariant(NormalizeSpecializations(specializations)));
    }

    /// <summary>Gets the compute pipeline for the given specialization arguments.</summary>
    /// <param name="specializations">Optional specialization arguments.</param>
    /// <returns>The compute pipeline owned by this shader.</returns>
    public GPUPipeline GetComputePipeline(params ReadOnlySpan<object> specializations)
    {
        Debug.Assert(!IsDestroyed);
        ShaderVariant variant = specializations.IsEmpty ? DefaultVariant : GetOrCompileVariant(NormalizeSpecializations(specializations));
        return variant.GetComputePipeline();
    }

    /// <summary>Gets or creates a shared graphics or mesh pipeline for the default variant.</summary>
    /// <param name="state">The pipeline-defining material state.</param>
    /// <param name="colorFormats">The render pass color target formats.</param>
    /// <param name="depthFormat">The render pass depth format.</param>
    /// <param name="stencilFormat">The render pass stencil format.</param>
    /// <returns>The pipeline; owned and destroyed by this shader.</returns>
    public GPUPipeline GetPipeline(in PipelineState state, ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat)
    {
        Debug.Assert(!IsDestroyed);
        return DefaultVariant.GetPipeline(state, colorFormats, depthFormat, stencilFormat);
    }

    /// <summary>Gets or creates a shared graphics or mesh pipeline for a specific specialization.</summary>
    /// <param name="state">The pipeline-defining material state.</param>
    /// <param name="colorFormats">The render pass color target formats.</param>
    /// <param name="depthFormat">The render pass depth format.</param>
    /// <param name="stencilFormat">The render pass stencil format.</param>
    /// <param name="specializations">The specialization arguments for the variant.</param>
    /// <returns>The pipeline; owned and destroyed by this shader.</returns>
    public GPUPipeline GetPipeline(in PipelineState state, ReadOnlySpan<Format> colorFormats, Format depthFormat, Format stencilFormat,
        params ReadOnlySpan<object> specializations)
    {
        Debug.Assert(!IsDestroyed);
        ShaderVariant variant = specializations.IsEmpty ? DefaultVariant : GetOrCompileVariant(NormalizeSpecializations(specializations));
        return variant.GetPipeline(state, colorFormats, depthFormat, stencilFormat);
    }

    internal ShaderVariant GetOrCompileVariant(string[] normalizedSpecializations)
    {
        Debug.Assert(!IsDestroyed);
        ShaderVariant? cached = FindVariant(Volatile.Read(ref _variants), normalizedSpecializations);
        if (cached != null)
            return cached;

        lock (_variantLock)
        {
            VariantEntry[] variants = _variants;
            cached = FindVariant(variants, normalizedSpecializations);
            if (cached != null)
                return cached;

            CompiledShader compiled = _compileVariant(normalizedSpecializations);
            ShaderVariant created = new(this, compiled, Kind, normalizedSpecializations);
            VariantEntry[] updated = new VariantEntry[variants.Length + 1];
            variants.CopyTo(updated, 0);
            updated[^1] = new VariantEntry { Key = normalizedSpecializations, Variant = created };
            Volatile.Write(ref _variants, updated);
            return created;
        }
    }

    /// <summary>Destroys all variants' pipelines.</summary>
    protected override void Release()
    {
        foreach (VariantEntry entry in _variants)
            entry.Variant.Destroy();
        _variants = [];
        _defaultVariant = null;
    }

    private static ShaderVariant? FindVariant(VariantEntry[] variants, ReadOnlySpan<string> specializations)
    {
        for (int index = 0; index < variants.Length; index++)
        {
            if (specializations.SequenceEqual(variants[index].Key))
                return variants[index].Variant;
        }
        return null;
    }

    private static bool HasGenericStages(ShaderModule module, ShaderKind kind)
    {
        ShaderStageKind stages = kind switch
        {
            ShaderKind.Graphics => ShaderStageKind.Vertex | ShaderStageKind.Pixel,
            ShaderKind.Compute => ShaderStageKind.Compute,
            ShaderKind.Mesh => ShaderStageKind.Task | ShaderStageKind.Mesh | ShaderStageKind.Pixel,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        foreach (ShaderModuleEntryPoint entryPoint in module.EntryPoints)
        {
            if ((entryPoint.Stage & stages) != 0 && entryPoint.HasGenericParameters)
                return true;
        }
        return false;
    }

    internal static ShaderPipelineKind ToPipelineKind(ShaderKind kind) => kind switch
    {
        ShaderKind.Graphics => ShaderPipelineKind.Graphics,
        ShaderKind.Compute => ShaderPipelineKind.Compute,
        ShaderKind.Mesh => ShaderPipelineKind.Mesh,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static ShaderKind FromPipelineKind(ShaderPipelineKind kind) => kind switch
    {
        ShaderPipelineKind.Graphics => ShaderKind.Graphics,
        ShaderPipelineKind.Compute => ShaderKind.Compute,
        ShaderPipelineKind.Mesh => ShaderKind.Mesh,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private struct VariantEntry
    {
        public string[] Key;
        public ShaderVariant Variant;
    }
}
