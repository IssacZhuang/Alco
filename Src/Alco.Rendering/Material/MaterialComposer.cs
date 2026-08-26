using System.Numerics;
using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// MaterialComposer: the pipeline-agnostic material-composition primitive. A
// "pass template" shader library owns generic [shader] entry points over a
// surface contract (interface); a "surface" library exports the concrete surface
// type. Composition is slang's own component system (composite + link-time
// specialization) — no generated wrapper modules, no preprocessor stitching:
//
//   shader = composer.ComposeGraphics(gbufferLibrary, mySurfaceLibrary);
//
// Every generic entry point takes the surface type as its first specialization
// argument; value specialization arguments (e.g. the shadow template's
// <let AlphaTest : bool>) feed the entries' value parameters in entry order.
// Composed shaders are cached per (template, surface, type, args, kind), ride
// the module system's disk caches, and hot-reload with it: when either library's
// module is invalidated the shader's caches are cleared and ShaderInvalidated
// fires so consumers can re-record static render bundles.
//
// The composer also owns the material-parameter convention: a surface marks its
// parameter cbuffers with the [MaterialParams] user attribute (free names, any
// number of blocks, scalar/vector float members may mix); the engine discovers
// them from slang's module-level reflection (GetParamsLayouts — no probe
// compile) and packs uniform buffers from named values (PackParamsBuffers).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Composes pass-template and surface shader libraries into cached, hot-reloadable shaders.</summary>
public sealed class MaterialComposer : IDisposable
{
    /// <summary>The surface type name every surface module exports by convention.</summary>
    public const string DefaultSurfaceTypeName = "Surface";

    /// <summary>
    /// The user-defined attribute a surface marks its material-parameter blocks with
    /// (<c>[MaterialParams] cbuffer ...</c>). Discovery is marker-driven, not
    /// name-driven: a surface names and splits its parameter blocks freely.
    /// </summary>
    public const string ParamsMarkerAttribute = "MaterialParams";

    private readonly record struct CompositionKey(
        ShaderLibrary Template,
        ShaderLibrary Surface,
        string SurfaceType,
        string Specialization,
        bool Compute);

    private readonly RenderingSystem _rendering;
    private readonly ShaderSystem _shaderSystem;
    private readonly Lock _lock = new();
    private readonly Dictionary<CompositionKey, Shader> _shaders = new();
    private readonly List<SlangProgram> _pinnedPrograms = [];
    private bool _disposed;

    /// <summary>Raised for each composed shader whose template or surface module was invalidated.</summary>
    public event Action<Shader>? ShaderInvalidated;

    /// <summary>
    /// Create the composer over a shader system's module system.
    /// </summary>
    /// <param name="rendering">The rendering system (shader factory, buffer creation).</param>
    /// <param name="shaderSystem">
    /// The shader system owning the slang module system; null uses the rendering system's
    /// shared one (the production path — tests may hand in an isolated instance).
    /// </param>
    public MaterialComposer(RenderingSystem rendering, ShaderSystem? shaderSystem = null)
    {
        _rendering = rendering;
        _shaderSystem = shaderSystem ?? rendering.ShaderSystem;
        _shaderSystem.Modules.ModulesInvalidated += OnModulesInvalidated;
    }

    /// <summary>
    /// The composed graphics (vertex+fragment) shader of one (template, surface) pair;
    /// created on first request, then cached. The composer owns the returned shader.
    /// </summary>
    /// <param name="template">The pass-template library (owns the generic entry points).</param>
    /// <param name="surface">The surface library (exports the surface type).</param>
    /// <param name="valueSpecArgs">Value specialization arguments in entry order (e.g. ["true"] for the shadow template's AlphaTest).</param>
    /// <param name="surfaceType">The companion type name; <see cref="DefaultSurfaceTypeName"/> by convention.</param>
    public Shader ComposeGraphics(
        ShaderLibrary template, ShaderLibrary surface,
        IReadOnlyList<string>? valueSpecArgs = null,
        string surfaceType = DefaultSurfaceTypeName)
        => Compose(template, surface, valueSpecArgs, surfaceType, compute: false);

    /// <summary>
    /// The composed compute shader of one (template, surface) pair — e.g. the voxel-GI
    /// feed whose template owns a single surface-generic [shader("compute")] entry.
    /// </summary>
    /// <inheritdoc cref="ComposeGraphics"/>
    public Shader ComposeCompute(
        ShaderLibrary template, ShaderLibrary surface,
        IReadOnlyList<string>? valueSpecArgs = null,
        string surfaceType = DefaultSurfaceTypeName)
        => Compose(template, surface, valueSpecArgs, surfaceType, compute: true);

    /// <summary>
    /// The material-parameter blocks of a surface library — every uniform block
    /// marked <c>[<see cref="ParamsMarkerAttribute"/>]</c>, with its scalar/vector
    /// float members — from the library's own reflection (no entry points, no
    /// link; cached by the module system). The material domain's view of
    /// <see cref="ShaderLibrary.Reflection"/>: blocks are found by the
    /// marker, and a marked block the float view cannot fully represent fails
    /// here (the parameter system writes floats only). Empty means the module
    /// marks no parameter blocks.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformMember>> GetParamsLayouts(ShaderLibrary surface)
    {
        Dictionary<string, IReadOnlyList<ShaderUniformMember>> lookup = new(StringComparer.Ordinal);
        foreach (ShaderUniformBlock block in surface.Reflection.UniformBlocks)
        {
            if (!block.Attributes.Contains(ParamsMarkerAttribute))
            {
                continue;
            }
            if (block.UnsupportedMemberReason != null)
            {
                throw new NotSupportedException(
                    $"Parameter block '{block.Name}' of '{surface.Name}' is marked [{ParamsMarkerAttribute}] " +
                    $"but {block.UnsupportedMemberReason}");
            }
            if (block.Members.Count == 0)
            {
                throw new NotSupportedException(
                    $"Parameter block '{block.Name}' of '{surface.Name}' is marked [{ParamsMarkerAttribute}] " +
                    "but declares no scalar/vector members; unmark it or add the members it should carry.");
            }
            lookup.Add(block.Name, block.Members);
        }
        return lookup;
    }

    /// <summary>
    /// Packs a uniform buffer from a parameter-block layout and named values: members
    /// the value table leaves out read zero; each value marshals to its member's
    /// reflected scalar kind (float components, int/uint/bool images, arrays,
    /// matrices); an unknown name is a typo and fails listing the valid members.
    /// The buffer is a <see cref="UniformGraphicsBuffer"/> laid out at the offsets
    /// slang reflected, written once and flushed — the compile-time material path
    /// of the per-frame uniform buffers the render nodes use.
    /// </summary>
    /// <param name="layout">The block members (<see cref="GetParamsLayouts"/>).</param>
    /// <param name="values">The values by member name.</param>
    /// <param name="name">The owner name (error context and buffer label).</param>
    public UniformGraphicsBuffer PackParamsBuffer(
        IReadOnlyList<ShaderUniformMember> layout,
        IReadOnlyDictionary<string, ShaderValue> values,
        string name)
    {
        foreach (string key in values.Keys)
        {
            if (layout.All(member => member.Name != key))
            {
                throw new InvalidDataException(
                    $"Parameter '{key}' of '{name}' matches no member of the surface's parameter block; expected one of: {string.Join(", ", layout.Select(member => member.Name))}.");
            }
        }

        UniformGraphicsBuffer buffer = _rendering.CreateUniformGraphicsBuffer(
            new ShaderUniformBlock($"{name}_params", [], layout), $"{name}_params");
        try
        {
            foreach (ShaderUniformMember member in layout)
            {
                if (values.TryGetValue(member.Name, out ShaderValue value))
                {
                    WriteMember(buffer, member, value, name);
                }
            }
            buffer.Flush();
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        return buffer;
    }

    // One authored value lands on one reflected member, kind-checked: a float
    // value takes the member's leading components (a lone integer lands on a
    // float member as its exact scalar); int/uint/bool values marshal their
    // 32-bit images; arrays fill the whole span element by element.
    private static void WriteMember(
        UniformGraphicsBuffer buffer, ShaderUniformMember member, ShaderValue value, string ownerName)
    {
        string context = $"Parameter '{member.Name}' of '{ownerName}'";
        if (member.ScalarType == ShaderUniformScalarType.Float32)
        {
            WriteFloatMember(buffer, member, value, context);
            return;
        }
        // int/uint/bool members: array elements sit at the reflected stride.
        uint stride = member.SizeBytes / member.ElementCount;
        if (member.ElementCount > 1)
        {
            if (value.ElementCount != member.ElementCount)
            {
                throw new InvalidDataException(
                    $"{context} is an array of {member.ElementCount} elements; the value has {value.ElementCount}.");
            }
            for (int element = 0; element < member.ElementCount; element++)
            {
                WriteElement(buffer, member, member.OffsetBytes + (uint)element * stride, value, element, context);
            }
            return;
        }
        WriteElement(buffer, member, member.OffsetBytes, value, 0, context);
    }

    // Float members marshal from the value's flat scalar list: a plain member
    // takes its leading components (the rest reads zero), an array member
    // takes exactly its whole component span — element-shaped values (three
    // float4s) and flat lists (twelve numbers) both fit.
    private static void WriteFloatMember(
        UniformGraphicsBuffer buffer, ShaderUniformMember member, ShaderValue value, string context)
    {
        if (value.Kind is not (ShaderValueKind.Float32 or ShaderValueKind.Int32 or ShaderValueKind.UInt32))
        {
            throw new InvalidDataException(
                $"{context} is a float member; the authored value is {value} (write a number or a color).");
        }
        // An authored integer ("speed": 2) writes into a float member as its
        // exact scalar.
        ReadOnlySpan<float> flat = value.Kind == ShaderValueKind.Float32
            ? value.AsFloatList()
            : [value.GetInt()];
        if (member.ComponentCount is < 1 or > 16)
        {
            throw new InvalidDataException(
                $"{context} takes {member.ComponentCount} components; material parameters support at most 16 (a matrix).");
        }
        if (member.ComponentCount == 16 && flat.Length != 16)
        {
            throw new InvalidDataException($"{context} is a matrix member; author it as a matrix.");
        }
        if (member.ElementCount > 1)
        {
            int components = member.ComponentCount;
            if (flat.Length != components * (int)member.ElementCount)
            {
                throw new InvalidDataException(
                    $"{context} is an array of {member.ElementCount} × {components} components; the value has {flat.Length} scalars.");
            }
            uint stride = member.SizeBytes / member.ElementCount;
            for (int element = 0; element < member.ElementCount; element++)
            {
                WriteFloatImage(buffer, member.OffsetBytes + (uint)element * stride,
                    flat.Slice(element * components, components));
            }
            return;
        }
        Span<float> image = stackalloc float[member.ComponentCount];
        image.Clear();
        // Leading components land (a Vector4 authored onto a float member reads
        // its first component); the rest of the member reads zero.
        flat[..Math.Min(flat.Length, image.Length)].CopyTo(image);
        WriteFloatImage(buffer, member.OffsetBytes, image);
    }

    private static void WriteElement(
        UniformGraphicsBuffer buffer, ShaderUniformMember member, uint offset,
        ShaderValue value, int element, string context)
    {
        switch (member.ScalarType)
        {
            case ShaderUniformScalarType.Int32:
            {
                if (value.Kind is not (ShaderValueKind.Int32 or ShaderValueKind.UInt32))
                {
                    throw new InvalidDataException(
                        $"{context} is an int member; the authored value is {value} (write an integer without a fraction).");
                }
                Span<int> image = stackalloc int[member.ComponentCount];
                image.Fill(value.GetInt(element));
                WriteIntImage(buffer, offset, image);
                break;
            }
            case ShaderUniformScalarType.UInt32:
            {
                if (value.Kind is not (ShaderValueKind.Int32 or ShaderValueKind.UInt32))
                {
                    throw new InvalidDataException(
                        $"{context} is a uint member; the authored value is {value} (write a non-negative integer).");
                }
                Span<uint> image = stackalloc uint[member.ComponentCount];
                image.Fill(unchecked((uint)value.GetInt(element)));
                WriteUintImage(buffer, offset, image);
                break;
            }
            case ShaderUniformScalarType.Bool32:
            {
                if (value.Kind != ShaderValueKind.Bool32)
                {
                    throw new InvalidDataException(
                        $"{context} is a bool member; the authored value is {value} (write true or false).");
                }
                if (member.ComponentCount == 1)
                {
                    Span<uint> image = [(uint)(value.GetInt(element) != 0 ? 1 : 0)];
                    WriteUintImage(buffer, offset, image);
                }
                else
                {
                    // No bool-vector vocabulary exists; marshal as uint components.
                    Span<uint> image = stackalloc uint[member.ComponentCount];
                    image.Clear();
                    image[0] = (uint)(value.GetInt(element) != 0 ? 1 : 0);
                    WriteUintImage(buffer, offset, image);
                }
                break;
            }
            default:
                throw new InvalidDataException($"{context} has unsupported scalar type {member.ScalarType}.");
        }
    }

    // Raw images write at the resolved (element) offset: the staging layout
    // matches the member's reflected component packing.
    private static unsafe void WriteFloatImage(
        UniformGraphicsBuffer buffer, uint offset, ReadOnlySpan<float> image)
    {
        fixed (float* ptr = image)
        {
            buffer.WriteRaw(offset, new ReadOnlySpan<byte>(ptr, image.Length * sizeof(float)));
        }
    }

    // Integer images of vectors (int2/uint3/...) write through a same-width
    // blit: the staging layout matches the member's reflected component packing.
    private static unsafe void WriteIntImage(
        UniformGraphicsBuffer buffer, uint offset, ReadOnlySpan<int> image)
    {
        fixed (int* ptr = image)
        {
            buffer.WriteRaw(offset, new ReadOnlySpan<byte>(ptr, image.Length * sizeof(int)));
        }
    }

    private static unsafe void WriteUintImage(
        UniformGraphicsBuffer buffer, uint offset, ReadOnlySpan<uint> image)
    {
        fixed (uint* ptr = image)
        {
            buffer.WriteRaw(offset, new ReadOnlySpan<byte>(ptr, image.Length * sizeof(uint)));
        }
    }

    /// <summary>
    /// Packs every parameter block of a surface (see <see cref="GetParamsLayouts"/>)
    /// from one value table: a value whose name matches no member of ANY block is a
    /// typo and fails listing the valid members; the same member name in two blocks
    /// is ambiguous and fails. Blocks without any matching value still pack (their
    /// members read zero).
    /// </summary>
    /// <returns>The packed buffer of each block, keyed by block name.</returns>
    public IReadOnlyDictionary<string, GraphicsBuffer> PackParamsBuffers(
        IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformMember>> layouts,
        IReadOnlyDictionary<string, ShaderValue> values,
        string name)
    {
        List<string> allMembers = [];
        foreach (KeyValuePair<string, IReadOnlyList<ShaderUniformMember>> block in layouts)
        {
            foreach (ShaderUniformMember member in block.Value)
            {
                allMembers.Add(member.Name);
            }
        }
        foreach (string key in values.Keys)
        {
            if (!allMembers.Contains(key))
            {
                throw new InvalidDataException(
                    $"Parameter '{key}' of '{name}' matches no member of any parameter block of the surface; expected one of: {string.Join(", ", allMembers)}.");
            }
        }
        if (allMembers.Count != allMembers.Distinct().Count())
        {
            string duplicate = allMembers.GroupBy(member => member).First(group => group.Count() > 1).Key;
            throw new InvalidDataException(
                $"Parameter member '{duplicate}' of '{name}' is declared by more than one parameter block of the surface; member names must be unique across blocks.");
        }

        Dictionary<string, GraphicsBuffer> buffers = new(StringComparer.Ordinal);
        try
        {
            foreach (KeyValuePair<string, IReadOnlyList<ShaderUniformMember>> block in layouts)
            {
                Dictionary<string, ShaderValue> blockValues = new(StringComparer.Ordinal);
                foreach (ShaderUniformMember member in block.Value)
                {
                    if (values.TryGetValue(member.Name, out ShaderValue value))
                    {
                        blockValues[member.Name] = value;
                    }
                }
                buffers.Add(block.Key, PackParamsBuffer(block.Value, blockValues, $"{name}:{block.Key}"));
            }
        }
        catch
        {
            foreach (GraphicsBuffer buffer in buffers.Values)
            {
                buffer.Dispose();
            }
            throw;
        }
        return buffers;
    }

    /// <summary>
    /// The texture slot names of a surface library — what the binding side fills from
    /// the material's texture table, with the asset's fallback policy for
    /// unbound slots. The name projection of the library's texture slots (which
    /// also carry each slot's required dimension and sample type); discovery
    /// stays name-keyed, no set number — a ParameterBlock's set is
    /// compiler-assigned declaration order, nothing the engine pins or reads.
    /// </summary>
    public IReadOnlyList<string> EnumerateTextureSlots(ShaderLibrary surface)
        => [.. surface.Reflection.TextureSlots.Select(slot => slot.Name)];

    private Shader Compose(
        ShaderLibrary template, ShaderLibrary surface,
        IReadOnlyList<string>? valueSpecArgs, string surfaceType, bool compute)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string[] specArgs = valueSpecArgs == null ? [] : [.. valueSpecArgs];
        string specKey = string.Join("|", specArgs);
        CompositionKey key = new(template, surface, surfaceType, specKey, compute);
        lock (_lock)
        {
            if (_shaders.TryGetValue(key, out Shader? cached))
            {
                return cached;
            }

            string shaderName = specArgs.Length == 0
                ? $"{template.Name}+{surface.Name}"
                : $"{template.Name}+{surface.Name}[{specKey}]";
            // A composed shader has no open specialization axis of its own — the
            // variant was fixed by the composition — so the handle ignores the
            // accessor-level specialization arguments.
            Shader shader = _rendering.CreateShader(
                shaderName,
                _ => CompilePermutation(key, specArgs, shaderName));
            _shaders.Add(key, shader);
            return shader;
        }
    }

    private ShaderModulesInfo CompilePermutation(CompositionKey key, string[] specArgs, string shaderName)
    {
        SlangModuleSystem modules = _shaderSystem.Modules;
        SlangProgram program = modules.GetComposedProgram(
            key.Template.Name, key.Surface.Name, key.SurfaceType, specArgs);

        // Programs stay pinned: ShaderModule structs reference the code arrays.
        lock (_lock)
        {
            _pinnedPrograms.Add(program);
        }

        ShaderModulesInfo info = ShaderSystem.BuildModulesInfo(
            _rendering, modules.Target, shaderName, program);
        if (key.Compute ? !info.IsComputeShader : !info.IsGraphicsShader)
        {
            throw new InvalidOperationException(
                $"Composed shader '{shaderName}' has the wrong stage mix for {(key.Compute ? "compute" : "graphics")}: " +
                $"the template module must own {(key.Compute ? "a single [shader(\"compute\")] entry" : "[shader(\"vertex\")] and [shader(\"fragment\")] entries")}.");
        }
        return info;
    }

    private void OnModulesInvalidated(IReadOnlyList<string> affectedModules)
    {
        List<Shader> affectedShaders;
        lock (_lock)
        {
            // Stale programs die with the session rebuild; pins are refreshed lazily.
            // Library reflection caches ride the module system's session rebuild.
            _pinnedPrograms.Clear();
            affectedShaders =
            [
                .. _shaders.Where(pair =>
                        affectedModules.Contains(pair.Key.Template.Name) ||
                        affectedModules.Contains(pair.Key.Surface.Name))
                           .Select(pair => pair.Value),
            ];
        }
        foreach (Shader shader in affectedShaders)
        {
            shader.UnsafeModuleReload();
            ShaderInvalidated?.Invoke(shader);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _shaderSystem.Modules.ModulesInvalidated -= OnModulesInvalidated;
        lock (_lock)
        {
            foreach (Shader shader in _shaders.Values)
            {
                shader.Dispose();
            }
            _shaders.Clear();
            _pinnedPrograms.Clear();
        }
    }
}
