using System.Globalization;
using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

/// <summary>
/// The material facility: a stateless factory that composes pass templates with
/// surface shader libraries into cached, hot-reloadable shaders, and compiles
/// data-only <see cref="MaterialAsset"/>s into per-pass GPU materials via
/// <see cref="Compile"/> (graphics) or <see cref="CompileCompute"/> (compute).
/// Composed shaders are cached per key and owned by this compiler; compiled
/// materials are caller-owned — every compile produces a fresh material the
/// caller shares across meshes and disposes. Dispose the compiler to release
/// the composed-shader cache; it owns nothing else.
/// </summary>
public sealed class MaterialCompiler : AutoDisposable
{
    /// <summary>
    /// The user-defined attribute a surface marks its material-parameter blocks with
    /// (<c>[MaterialParams] cbuffer ...</c>). Discovery is marker-driven, not
    /// name-driven: a surface names and splits its parameter blocks freely.
    /// </summary>
    public const string ParamsMarkerAttribute = "MaterialParams";

    private readonly record struct CompositionKey(
        ShaderLibrary Template,
        ShaderLibrary Surface,
        string Specialization,
        bool Compute);

    private readonly RenderingSystem _rendering;
    private readonly ShaderLibrary? _defaultSurface;
    private readonly ShaderSystem _shaderSystem;
    private readonly Lock _lock = new();
    private readonly Dictionary<CompositionKey, Shader> _shaders = new();
    private readonly List<SlangProgram> _pinnedPrograms = [];

    /// <summary>
    /// Create the compiler. The renderers/features that need materials compile
    /// through it with their own templates and factories.
    /// </summary>
    /// <param name="rendering">The rendering system (material factory, fallback textures, shared ShaderSystem).</param>
    /// <param name="defaultSurface">
    /// The pipeline family's default surface library, composed when a material names no
    /// <see cref="MaterialAsset.Surface"/> (e.g. World3D's PbrStandard); null requires
    /// every material to name its surface.
    /// </param>
    /// <param name="shaderSystem">
    /// The shader system owning the slang module system; null uses the rendering system's
    /// shared one (the production path — tests may hand in an isolated instance).
    /// </param>
    public MaterialCompiler(
        RenderingSystem rendering, ShaderLibrary? defaultSurface = null, ShaderSystem? shaderSystem = null)
    {
        _rendering = rendering;
        _defaultSurface = defaultSurface;
        _shaderSystem = shaderSystem ?? rendering.ShaderSystem;
        _shaderSystem.Modules.ModulesInvalidated += OnModulesInvalidated;
    }

    /// <summary>Raised for each composed shader whose template or surface module was invalidated.</summary>
    public event Action<Shader>? ShaderInvalidated;

    /// <summary>
    /// The composed graphics (vertex+fragment) shader of one (template, surface) pair;
    /// created on first request, then cached. The compiler owns the returned shader.
    /// </summary>
    /// <param name="template">The pass-template library (owns the generic entry points).</param>
    /// <param name="surface">The surface library (exports the contract's single conforming type).</param>
    /// <param name="specializations">
    /// Named specialization values for the template's generic value parameters (see
    /// <see cref="BuildSpecializationLiterals"/>); null selects every axis's default.
    /// </param>
    public Shader ComposeGraphics(
        ShaderLibrary template, ShaderLibrary surface,
        IReadOnlyDictionary<string, ShaderValue>? specializations = null)
        => Compose(template, surface, specializations, compute: false);

    /// <summary>
    /// The composed compute shader of one (template, surface) pair — e.g. the voxel-GI
    /// feed whose template owns a single surface-generic [shader("compute")] entry.
    /// </summary>
    /// <inheritdoc cref="ComposeGraphics"/>
    public Shader ComposeCompute(
        ShaderLibrary template, ShaderLibrary surface,
        IReadOnlyDictionary<string, ShaderValue>? specializations = null)
        => Compose(template, surface, specializations, compute: true);

    /// <summary>
    /// The link-time specialization literals of one (template, named values) pair: each
    /// axis the template's reflection declares, in specialization argument order, takes
    /// the value the table assigns to its name — formatted per the axis's reflected
    /// scalar kind — and its type's default when the table omits it. This is the single
    /// translation between the material domain's named values and the positional
    /// argument lists the slang compile paths consume.
    /// </summary>
    /// <param name="template">The pass-template library whose axes the values feed.</param>
    /// <param name="specializations">The named values (an asset's <see cref="MaterialAsset.Specializations"/>); null or empty selects every axis's default.</param>
    /// <returns>The specialization literals in argument order.</returns>
    /// <exception cref="InvalidDataException">The table names an axis the template does not declare, or a value's kind or shape does not fit the axis's scalar type.</exception>
    public static string[] BuildSpecializationLiterals(
        ShaderLibrary template, IReadOnlyDictionary<string, ShaderValue>? specializations)
    {
        IReadOnlyList<ShaderSpecializationAxis> axes = template.Reflection.SpecializationAxes;
        if (specializations is { Count: > 0 })
        {
            foreach (string key in specializations.Keys)
            {
                if (axes.All(axis => axis.Name != key))
                {
                    throw new InvalidDataException(
                        $"Specialization '{key}' matches no generic value parameter of template '{template.Name}'; expected one of: " +
                        (axes.Count == 0 ? "none" : string.Join(", ", axes.Select(axis => axis.Name))) + ".");
                }
            }
        }

        string[] literals = new string[axes.Count];
        for (int i = 0; i < axes.Count; i++)
        {
            literals[i] = specializations != null
                && specializations.TryGetValue(axes[i].Name, out ShaderValue value)
                ? FormatSpecializationLiteral(axes[i], value, template.Name)
                : axes[i].ScalarType == ShaderSpecScalarType.Bool ? "false" : "0";
        }
        return literals;
    }

    // One authored value formats as one slang literal, kind-checked against the
    // axis's reflected scalar type — the parameter packing's strictness, applied
    // to the specialization domain (an int token accepts a uint axis because JSON
    // cannot author a distinct unsigned value).
    private static string FormatSpecializationLiteral(
        ShaderSpecializationAxis axis, ShaderValue value, string templateName)
    {
        if (value.ComponentCount != 1 || value.ElementCount != 1)
        {
            throw new InvalidDataException(
                $"Specialization '{axis.Name}' of template '{templateName}' is a scalar {axis.ScalarType} axis; the authored value {value} must be a single scalar.");
        }
        return axis.ScalarType switch
        {
            ShaderSpecScalarType.Bool => value.Kind == ShaderValueKind.Bool32
                ? value.GetInt() != 0 ? "true" : "false"
                : throw InvalidSpecKind(axis, value, templateName, "true or false"),
            ShaderSpecScalarType.Int32 => value.Kind == ShaderValueKind.Int32
                ? value.GetInt().ToString(CultureInfo.InvariantCulture)
                : throw InvalidSpecKind(axis, value, templateName, "an integer"),
            _ => value.Kind is ShaderValueKind.Int32 or ShaderValueKind.UInt32
                ? value.GetInt() >= 0
                    ? ((uint)value.GetInt()).ToString(CultureInfo.InvariantCulture)
                    : throw new InvalidDataException(
                        $"Specialization '{axis.Name}' of template '{templateName}' is a uint axis; {value.GetInt()} is negative.")
                : throw InvalidSpecKind(axis, value, templateName, "a non-negative integer"),
        };
    }

    private static InvalidDataException InvalidSpecKind(
        ShaderSpecializationAxis axis, ShaderValue value, string templateName, string expected)
        => new($"Specialization '{axis.Name}' of template '{templateName}' is a {axis.ScalarType} axis; the authored value {value} is not {expected}.");

    /// <summary>
    /// Compile the material of an asset for one graphics pass: the pass template
    /// composes with the asset's surface, specialized by the asset's named
    /// <see cref="MaterialAsset.Specializations"/> table, and the caller's factory
    /// creates the GPU material applying the pass-mandated state (depth/blend/rasterizer,
    /// internal buffer bindings). Every call compiles a fresh material — the
    /// caller owns it: share it across the meshes using the asset, dispose it
    /// with the owning scene/renderer, or drop it for the GC.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <param name="template">The pass-template library, composed with the asset's surface.</param>
    /// <param name="createMaterial">The caller's factory: turns the composed shader into the pass's GPU material.</param>
    /// <returns>The caller-owned material of the (asset, template) pair.</returns>
    /// <exception cref="InvalidDataException">A texture slot or parameter of the asset matches nothing on the surface, or a specialization matches no axis of the template.</exception>
    public GraphicsMaterial Compile(
        MaterialAsset asset,
        ShaderLibrary template,
        Func<MaterialAsset, Shader, GraphicsMaterial> createMaterial)
        => Compile(asset, template, asset.Specializations, createMaterial);

    /// <summary>
    /// Compile with a facility-provided specialization table — the compile of
    /// <see cref="Compile(MaterialAsset, ShaderLibrary, Func{MaterialAsset, Shader, GraphicsMaterial})"/>
    /// for facilities that derive a template's variant from their own logic instead of
    /// the asset's authored table (e.g. the shadow pass specializing its AlphaTest axis
    /// from the material's alpha mode), or that deliberately take the unspecialized
    /// variant of a template with no axes of its own (e.g. the UI draw of an entity
    /// material, whose world-route axes do not apply).
    /// </summary>
    /// <param name="specializations">The named specialization values; null compiles the unspecialized variant (every axis of the template defaults).</param>
    /// <inheritdoc cref="Compile(MaterialAsset, ShaderLibrary, Func{MaterialAsset, Shader, GraphicsMaterial})"/>
    public GraphicsMaterial Compile(
        MaterialAsset asset,
        ShaderLibrary template,
        IReadOnlyDictionary<string, ShaderValue>? specializations,
        Func<MaterialAsset, Shader, GraphicsMaterial> createMaterial)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(createMaterial);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        Shader shader = Compose(template, SurfaceOf(asset), specializations, compute: false);
        ShaderReflection reflection = shader.GetShaderModules().ReflectionInfo;

        // Compile-time slot validation: a texture slot the surface does not
        // declare is a typo in the asset — fail here, at compile time.
        // (Slot discovery: see EnumerateTextureSlots.)
        IReadOnlyList<string> textureSlots = EnumerateTextureSlots(SurfaceOf(asset));
        foreach (string slot in asset.Textures.Keys)
        {
            if (!textureSlots.Contains(slot))
            {
                throw new InvalidDataException(
                    $"GraphicsMaterial '{asset.Name}' texture slot '{slot}' matches no texture of surface '{SurfaceOf(asset).Name}'; " +
                    $"expected one of: {string.Join(", ", textureSlots)}.");
            }
        }

        GraphicsMaterial material = createMaterial(asset, shader);
        try
        {
            // The parameter blocks, packed from the asset's values; each block is
            // bound where the pass's reflection keeps it (a pass that never samples
            // the block's consumers strips it from its layout). Like every bound
            // slot value, the packed buffers are escapable shared references
            // (ShaderParameterSet.TryGetBuffer) — nobody disposes them explicitly;
            // their finalizer reclaims them once nothing references them.
            foreach (KeyValuePair<string, GraphicsBuffer> block in PackParamsBuffers(asset))
            {
                if (reflection.TryGetResourceId(block.Key, out _))
                {
                    material.SetBuffer(block.Key, block.Value);
                }
            }

            // Bind every surface texture slot from the asset's own bindings, with
            // the asset's fallback policy for unbound slots; specialization folds
            // keep the full surface resource set in the layout, so the binding
            // side always sees every slot.
            foreach (string resource in textureSlots)
            {
                Texture2D? texture = asset.Textures.GetValueOrDefault(resource);
                material.SetTexture(resource, texture ?? ResolveFallbackTexture(asset, resource));
            }
        }
        catch
        {
            material.Dispose();
            throw;
        }
        return material;
    }

    /// <summary>
    /// The shader of one pass template composed with an asset's surface (the compiler's
    /// default surface when <paramref name="asset"/> is null or names none), specialized
    /// by the asset's <see cref="MaterialAsset.Specializations"/> — the composition step
    /// of <see cref="Compile"/>, on its own for inspection and tests.
    /// </summary>
    /// <param name="asset">The material asset whose surface composes; null selects the default surface.</param>
    /// <param name="template">The pass-template library.</param>
    public Shader ComposeSurfaceShader(MaterialAsset? asset, ShaderLibrary template)
        => ComposeGraphics(template, SurfaceOf(asset), asset?.Specializations);

    /// <summary>
    /// The compute counterpart of <see cref="ComposeSurfaceShader"/>, for facilities
    /// whose surface feed is a compute pass (e.g. a voxel GI's voxelization).
    /// </summary>
    /// <param name="asset">The material asset whose surface composes; null selects the default surface.</param>
    /// <param name="template">The pass-template library.</param>
    public Shader ComposeSurfaceComputeShader(MaterialAsset? asset, ShaderLibrary template)
        => ComposeCompute(template, SurfaceOf(asset), asset?.Specializations);

    /// <summary>
    /// The compute counterpart of <see cref="Compile"/>: the material of an asset for a
    /// compute pass template (e.g. a voxel GI's voxelization), under the same slot rules
    /// as the graphics passes — texture slots are validated against the composed
    /// reflection and bound from the asset's own bindings (the asset's fallback policy
    /// for unbound slots), and the surface's parameter blocks are packed and bound.
    /// <br/>The material is caller-owned: share it across the dispatches using the asset
    /// and drop it with the owning facility; a compute material holds no disposable
    /// state of its own, and the GPU resources it references finalize themselves.
    /// </summary>
    /// <param name="asset">The material asset; its fallback policy covers unbound slots.</param>
    /// <param name="template">The pass-template library.</param>
    /// <returns>The caller-owned compute material, fully bound except facility data.</returns>
    /// <exception cref="InvalidDataException">A texture slot or parameter of the asset matches nothing on the surface, or a specialization matches no axis of the template.</exception>
    public ComputeMaterial CompileCompute(MaterialAsset asset, ShaderLibrary template)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Shader shader = ComposeSurfaceComputeShader(asset, template);
        ShaderReflection reflection = shader.GetShaderModules().ReflectionInfo;

        // Compile-time slot validation, the same rule as the graphics passes: a
        // texture slot the surface does not declare is a typo in the asset.
        IReadOnlyList<string> textureSlots = EnumerateTextureSlots(SurfaceOf(asset));
        foreach (string slot in asset.Textures.Keys)
        {
            if (!textureSlots.Contains(slot))
            {
                throw new InvalidDataException(
                    $"Compute material '{asset.Name}' texture slot '{slot}' matches no texture of surface '{SurfaceOf(asset).Name}'; " +
                    $"expected one of: {string.Join(", ", textureSlots)}.");
            }
        }

        ComputeMaterial material = _rendering.CreateComputeMaterial(shader);
        foreach (KeyValuePair<string, GraphicsBuffer> block in PackParamsBuffers(asset))
        {
            if (reflection.TryGetResourceId(block.Key, out _))
            {
                material.SetBuffer(block.Key, block.Value);
            }
        }

        // Bind every surface texture slot from the asset's own bindings, with the
        // asset's fallback policy for unbound slots — the bindings are final: streamed
        // textures upload in place and are never replaced.
        foreach (string resource in textureSlots)
        {
            Texture2D? texture = asset.Textures.GetValueOrDefault(resource);
            material.SetTexture(resource, texture ?? ResolveFallbackTexture(asset, resource));
        }
        return material;
    }

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
        IReadOnlyDictionary<string, ShaderValue>? specializations, bool compute)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        string[] specArgs = BuildSpecializationLiterals(template, specializations);
        string specKey = string.Join("|", specArgs);
        CompositionKey key = new(template, surface, specKey, compute);
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
        // The surface type is discovered inside (contract from the template's
        // generic entry points, the companion's single conformer by subtype
        // reflection) — no type name crosses this boundary.
        SlangProgram program = modules.GetComposedProgram(
            key.Template.Name, key.Surface.Name, specArgs);

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

    /// <summary>
    /// The fallback texture of one surface texture resource of an asset — the asset's
    /// own policy (<see cref="MaterialAsset.GetTextureFallback"/>) resolved to a device
    /// texture, for the unbound slots of a compile.
    /// </summary>
    /// <param name="asset">The material asset whose policy resolves.</param>
    /// <param name="resourceName">The shader resource name of the texture slot.</param>
    private Texture2D ResolveFallbackTexture(MaterialAsset asset, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.GetTextureFallback(resourceName) switch
        {
            MaterialTextureFallback.Black => _rendering.TextureBlack,
            MaterialTextureFallback.FlatNormal => _rendering.TextureFlatNormal,
            _ => _rendering.TextureWhite,
        };
    }

    /// <summary>
    /// The parameter buffers of an asset: every block of its surface marked
    /// <c>[MaterialParams]</c> (free names, any number), packed from
    /// <see cref="MaterialAsset.Parameters"/> by member name at the offsets slang
    /// reflected. Packed once per (asset, surface) and cached on the asset — every
    /// pass compiles the same bytes, so the passes share the buffers (see
    /// <see cref="MaterialAsset.ParameterBuffers"/>); re-setting the asset's
    /// surface/parameters drops the cache for a fresh pack.
    /// </summary>
    private IReadOnlyDictionary<string, GraphicsBuffer> PackParamsBuffers(MaterialAsset asset)
    {
        ShaderLibrary surface = SurfaceOf(asset);
        if (asset.HasParameterBuffers(surface))
        {
            return asset.ParameterBuffers!;
        }
        IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformMember>> layouts =
            GetParamsLayouts(surface);
        if (layouts.Count == 0)
        {
            if (asset.Parameters.Count > 0)
            {
                throw new InvalidDataException(
                    $"GraphicsMaterial '{asset.Name}' has parameters, but its surface '{surface.Name}' " +
                    $"declares no [{ParamsMarkerAttribute}] parameter block.");
            }
            return new Dictionary<string, GraphicsBuffer>();
        }
        IReadOnlyDictionary<string, GraphicsBuffer> buffers =
            PackParamsBuffers(layouts, asset.Parameters, asset.Name);
        asset.SetParameterBuffers(surface, buffers);
        return buffers;
    }

    /// <summary>The surface library an asset composes with: its own, or the compiler's default.</summary>
    /// <exception cref="InvalidDataException">The asset names no surface and the compiler has no default.</exception>
    public ShaderLibrary SurfaceOf(MaterialAsset? asset)
    {
        ShaderLibrary? surface = asset?.Surface ?? _defaultSurface;
        if (surface == null)
        {
            throw new InvalidDataException(asset == null
                ? "No surface library named and the compiler has no default surface."
                : $"GraphicsMaterial '{asset.Name}' names no surface and the compiler has no default surface.");
        }
        return surface;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Compiled materials are caller-owned; the compiler owns only the
            // composed-shader cache.
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
}
