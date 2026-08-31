using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Translates a slang ProgramLayout (SlangReflection*) into the engine's
// ShaderReflection vocabulary:
//   - compute thread group size comes from the entry point reflection
//   - storage image formats come from binding-range queries on the global params layout
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One member of a slang uniform block is <see cref="ShaderUniformMember"/> in
/// Alco.Graphics — the engine-shaped reflection vocabulary this reader fills in.
/// </summary>
public static class SlangReflectionReader
{
    /// <summary>
    /// Builds the engine reflection info (bind groups, vertex layout, push
    /// constants, fragment output count, thread group size) from a slang
    /// program layout. One slang program contains all entry points, so a
    /// single layout covers the whole shader.
    /// </summary>
    public static unsafe ShaderReflection BuildReflectionInfo(IntPtr reflection)
    {
        List<(uint Space, BindGroupEntryInfo Entry)> entries = [];
        List<PushConstantsRange> pushConstants = [];
        List<ShaderUniformBlock> uniformBlocks = [];
        Dictionary<string, PixelFormat> imageFormats = CollectImageFormats(reflection);

        // Every binding gets the engine's Standard (V|F|C) visibility — the
        // Conservative visibility for parameters Slang reports outside an entry-point layout.
        // (ResolveEffectiveStage): pipeline layouts must stay supersets of the
        // device's default bind groups (e.g. default_bind_group_buffer), which
        // are created with Standard visibility.
        ShaderStage visibility = ShaderStage.None;
        ThreadGroupSize threadGroupSize = ThreadGroupSize.Default;
        nuint entryPointCount = SlangNative.spReflection_getEntryPointCount(reflection);
        for (nuint i = 0; i < entryPointCount; i++)
        {
            IntPtr entryPoint = SlangNative.spReflection_getEntryPointByIndex(reflection, i);
            if (entryPoint == IntPtr.Zero)
            {
                continue;
            }
            switch (SlangNative.spReflectionEntryPoint_getStage(entryPoint))
            {
                case SlangNative.SLANG_STAGE_VERTEX:
                    visibility |= ShaderStage.Vertex;
                    break;
                case SlangNative.SLANG_STAGE_FRAGMENT:
                    visibility |= ShaderStage.Fragment;
                    break;
                case SlangNative.SLANG_STAGE_COMPUTE:
                    visibility |= ShaderStage.Compute;
                    threadGroupSize = ReadThreadGroupSize(entryPoint);
                    break;
            }
        }
        if (visibility != ShaderStage.None)
        {
            visibility = ShaderStage.Standard;
        }
        else
        {
            visibility = ShaderStage.Vertex | ShaderStage.Fragment;
        }

        uint parameterCount = SlangNative.spReflection_GetParameterCount(reflection);
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflection_GetParameterByIndex(reflection, i);
            if (parameter == IntPtr.Zero)
            {
                continue;
            }

            string? name = VariableLayoutName(parameter);
            if (name == null)
            {
                continue;
            }

            IntPtr typeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(parameter);
            int kind = SlangNative.spReflectionTypeLayout_getKind(typeLayout);

            if (kind == SlangNative.SLANG_TYPE_KIND_CONSTANT_BUFFER)
            {
                if (IsPushConstant(typeLayout))
                {
                    uint size = (uint)SlangNative.spReflectionTypeLayout_GetSize(
                        SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout),
                        SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
                    pushConstants.Add(new PushConstantsRange(0, size));
                    continue;
                }

                // A block carries ordinary (uniform) data as one buffer, plus any
                // number of resource members the compiler assigns bindings after
                // that buffer, in declaration order. A block without uniform data
                // emits no buffer in SPIR-V, so it must not claim a binding here.
                uint uniformSize = (uint)SlangNative.spReflectionTypeLayout_GetSize(
                    SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout),
                    SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
                if (uniformSize > 0)
                {
                    entries.Add((SlangNative.spReflectionParameter_GetBindingSpace(parameter), new BindGroupEntryInfo
                    {
                        Entry = new BindGroupEntry(
                            SlangNative.spReflectionParameter_GetBindingIndex(parameter),
                            visibility,
                            BindingType.UniformBuffer,
                            name: name),
                        Size = uniformSize,
                    }));
                    AddUniformBlock(parameter, typeLayout, uniformBlocks);
                }

                AddBlockResourceMembers(parameter, typeLayout, entries, imageFormats, visibility);
                continue;
            }

            if (kind == SlangNative.SLANG_TYPE_KIND_PARAMETER_BLOCK)
            {
                // One ParameterBlock owns one whole set under automatic layout:
                // the block parameter's binding index IS its absolute set (its
                // space stays 0 — there is no register annotation to report),
                // so the set comes from GetBindingIndex, not GetBindingSpace.
                // Ordinary data becomes an automatically-introduced uniform
                // buffer at binding 0; resource members continue after it in
                // declaration order and their binding indices already account
                // for the shift. Members are flattened under their bare field
                // names — the same contract as set-scoped cbuffer members.
                uint blockSet = (uint)SlangNative.spReflectionParameter_GetBindingIndex(parameter);
                IntPtr elementLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
                uint uniformSize = (uint)SlangNative.spReflectionTypeLayout_GetSize(
                    elementLayout, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
                if (uniformSize > 0)
                {
                    entries.Add((blockSet, new BindGroupEntryInfo
                    {
                        Entry = new BindGroupEntry(
                            0u,
                            visibility,
                            BindingType.UniformBuffer,
                            name: name),
                        Size = uniformSize,
                    }));
                    AddUniformBlock(parameter, typeLayout, uniformBlocks);
                }

                AddResourceFields(elementLayout, blockSet, bindingBase: 0, prefix: null, entries, imageFormats, visibility);
                continue;
            }

            if (kind == SlangNative.SLANG_TYPE_KIND_SAMPLER_STATE)
            {
                // SamplerComparisonState is a distinct slang type (SPIR-V carries no
                // comparison marker — naga derives it from Dref usage), so the
                // declared type name is the reflection fact.
                entries.Add((SlangNative.spReflectionParameter_GetBindingSpace(parameter), new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(
                        SlangNative.spReflectionParameter_GetBindingIndex(parameter),
                        visibility,
                        IsComparisonSampler(typeLayout) ? BindingType.SamplerComparison : BindingType.Sampler,
                        name: name),
                }));
                continue;
            }

            if (kind == SlangNative.SLANG_TYPE_KIND_RESOURCE ||
                kind == SlangNative.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER)
            {
                AddResourceEntry(parameter, typeLayout, kind, name, entries, imageFormats, visibility);
                continue;
            }

            throw new NotSupportedException(
                $"Slang parameter '{name}' has unsupported type kind {kind}; the reflection bridge handles "
                + "constant buffers, push constants, textures, samplers and structured buffers.");
        }

        // The by-name contract needs unique resource names across the whole
        // program; blocks (and multi-module programs) make accidental shadowing
        // possible, so surface it at compile time instead of silently dropping
        // one of the duplicates from the name index.
        HashSet<string> seenNames = new(StringComparer.Ordinal);
        foreach ((uint _, BindGroupEntryInfo entry) in entries)
        {
            if (entry.Entry.Type is BindingType.Sampler or BindingType.SamplerComparison)
            {
                continue;
            }
            if (!seenNames.Add(entry.Entry.Name))
            {
                throw new ShaderReflectionException(
                    $"Duplicate shader resource name '{entry.Entry.Name}'; resource names must be unique across all sets of a program.");
            }
        }

        IReadOnlyList<BindGroupLayout> bindGroups = GroupBySpace(entries);
        IReadOnlyList<VertexInputLayout> vertexLayouts = BuildVertexLayouts(reflection);
        int fragmentOutputCount = CountFragmentOutputs(reflection);

        return new ShaderReflection(
            vertexLayouts, bindGroups, pushConstants, threadGroupSize, fragmentOutputCount, uniformBlocks);
    }

    /// <summary>
    /// Collects one uniform/parameter block into the shared block vocabulary:
    /// the parameter's user-defined attributes, the float-shaped members at
    /// their reflected offsets, and why any member the float view cannot
    /// represent is missing. The same helper feeds the library view
    /// (<see cref="BuildLibraryReflection"/>) and the linked view
    /// (<see cref="BuildReflectionInfo"/>).
    /// </summary>
    private static unsafe void AddUniformBlock(
        IntPtr parameter, IntPtr typeLayout, List<ShaderUniformBlock> blocks)
    {
        string? name = VariableLayoutName(parameter);
        if (name == null)
        {
            return;
        }
        List<ShaderUniformMember> members = ReadUniformMembers(typeLayout, out string? unsupportedReason);
        IntPtr variable = SlangNative.spReflectionVariableLayout_GetVariable(parameter);
        blocks.Add(new ShaderUniformBlock(
            name, GetUserAttributeNames(variable), members, unsupportedReason));
    }

    /// <summary>
    /// The members of the named uniform block of a LINKED program, at their
    /// post-link offsets — the strict wrapper over the block view collected by
    /// <see cref="BuildReflectionInfo"/>. Empty when the program declares no
    /// such block. Non-float members make the method throw — the material
    /// parameter system writes floats only.
    /// </summary>
    public static unsafe List<ShaderUniformMember> GetUniformMembers(IntPtr reflection, string cbufferName)
    {
        uint parameterCount = SlangNative.spReflection_GetParameterCount(reflection);
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflection_GetParameterByIndex(reflection, i);
            if (parameter == IntPtr.Zero || VariableLayoutName(parameter) != cbufferName)
            {
                continue;
            }

            IntPtr typeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(parameter);
            int kind = SlangNative.spReflectionTypeLayout_getKind(typeLayout);
            if (kind != SlangNative.SLANG_TYPE_KIND_CONSTANT_BUFFER
                && kind != SlangNative.SLANG_TYPE_KIND_PARAMETER_BLOCK)
            {
                continue;
            }

            return ReadUniformMembers(typeLayout);
        }
        return [];
    }

    /// <summary>
    /// Builds the module-level library reflection — every uniform/parameter
    /// block the layout declares, with its user-defined attributes and
    /// float-shaped members, plus every sampled-texture slot — from a slang
    /// module layout (no entry points, no link), and the module's
    /// specialization axes from its declaration tree. Domain-neutral:
    /// attribute markers (e.g. MaterialParams) are filtered by the caller, and
    /// a block whose members do not all fit the float view is reported through
    /// the block, not rejected here.
    /// </summary>
    /// <param name="layout">The module's own layout (blocks and slots).</param>
    /// <param name="moduleDecl">The module's declaration tree root (specialization axes).</param>
    /// <param name="moduleName">The module's name, for error context.</param>
    public static unsafe ShaderLibraryReflection BuildLibraryReflection(
        IntPtr layout, IntPtr moduleDecl, string moduleName)
    {
        List<ShaderUniformBlock> blocks = [];
        List<ShaderTextureSlot> textureSlots = [];
        List<ShaderSamplerSlot> samplerSlots = [];
        // Sample-type refinement needs the same declared image formats the
        // linked path consults (e.g. an r32uint storage-typed texture reads Uint).
        Dictionary<string, PixelFormat> imageFormats = CollectImageFormats(layout);
        uint parameterCount = SlangNative.spReflection_GetParameterCount(layout);
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflection_GetParameterByIndex(layout, i);
            if (parameter == IntPtr.Zero)
            {
                continue;
            }

            IntPtr typeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(parameter);
            int kind = SlangNative.spReflectionTypeLayout_getKind(typeLayout);
            if (kind != SlangNative.SLANG_TYPE_KIND_CONSTANT_BUFFER
                && kind != SlangNative.SLANG_TYPE_KIND_PARAMETER_BLOCK)
            {
                continue;
            }

            // Module level: every declared block counts, uniform data or not
            // (a resource-only block still contributes its texture slots).
            AddUniformBlock(parameter, typeLayout, blocks);

            IntPtr elementLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
            CollectTextureSlots(elementLayout, textureSlots, imageFormats);
            CollectSamplerSlots(elementLayout, samplerSlots);
        }
        return new ShaderLibraryReflection(
            blocks, textureSlots, samplerSlots, BuildSpecializationAxes(moduleDecl, moduleName));
    }

    /// <summary>
    /// The specialization axes of a module's generic entry points: every
    /// <c>let</c> value parameter of every module-scope generic function (the
    /// entry-point shape — a generic struct is not an entry), in declaration
    /// order. This is the same order the compile paths consume specialization
    /// arguments in, so a positional argument list is a projection of this one.
    /// A value parameter of a scalar kind the material domain cannot bind is
    /// an error here — the contract stays "every reflected axis is bindable".
    /// </summary>
    /// <param name="moduleDecl">The module's declaration tree root.</param>
    /// <param name="moduleName">The module's name, for error context.</param>
    private static List<ShaderSpecializationAxis> BuildSpecializationAxes(IntPtr moduleDecl, string moduleName)
    {
        List<ShaderSpecializationAxis> axes = [];
        uint childCount = SlangNative.spReflectionDecl_getChildrenCount(moduleDecl);
        for (uint i = 0; i < childCount; i++)
        {
            IntPtr child = SlangNative.spReflectionDecl_getChild(moduleDecl, i);
            if (SlangNative.spReflectionDecl_getKind(child) != SlangNative.SLANG_DECL_KIND_GENERIC)
            {
                continue;
            }
            IntPtr generic = SlangNative.spReflectionDecl_castToGeneric(child);
            if (generic == IntPtr.Zero)
            {
                continue;
            }
            // Generic entry points are generic functions; a generic struct
            // (e.g. an aggregation helper) is not an entry and carries no axes.
            IntPtr inner = SlangNative.spReflectionGeneric_GetInnerDecl(generic);
            if (inner == IntPtr.Zero ||
                SlangNative.spReflectionDecl_getKind(inner) != SlangNative.SLANG_DECL_KIND_FUNC)
            {
                continue;
            }

            uint valueCount = SlangNative.spReflectionGeneric_GetValueParameterCount(generic);
            for (uint v = 0; v < valueCount; v++)
            {
                IntPtr parameter = SlangNative.spReflectionGeneric_GetValueParameter(generic, v);
                if (parameter == IntPtr.Zero)
                {
                    continue;
                }
                string name = SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(parameter)) ?? "?";
                IntPtr type = SlangNative.spReflectionVariable_GetType(parameter);
                int scalar = type == IntPtr.Zero
                    ? SlangNative.SLANG_SCALAR_TYPE_NONE
                    : SlangNative.spReflectionType_GetScalarType(type);
                axes.Add(new ShaderSpecializationAxis(name, scalar switch
                {
                    SlangNative.SLANG_SCALAR_TYPE_BOOL => ShaderSpecScalarType.Bool,
                    SlangNative.SLANG_SCALAR_TYPE_INT32 => ShaderSpecScalarType.Int32,
                    SlangNative.SLANG_SCALAR_TYPE_UINT32 => ShaderSpecScalarType.UInt32,
                    _ => throw new ShaderCompilationException(
                        $"slang module '{moduleName}': generic entry point value parameter '{name}' has scalar kind " +
                        $"{scalar}; the material specialization domain supports bool, int and uint value parameters."),
                }));
            }
        }
        return axes;
    }

    /// <summary>
    /// The sampler members of a block's element struct, in declaration order,
    /// with the comparison flag — the custom samplers a material may bind by
    /// name (<c>SetSampler</c>). The engine's shared sampler bank members are
    /// engine-owned state, never bindable material slots.
    /// </summary>
    private static unsafe void CollectSamplerSlots(IntPtr structLayout, List<ShaderSamplerSlot> slots)
    {
        uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(structLayout);
        for (uint field = 0; field < fieldCount; field++)
        {
            IntPtr fieldLayout = SlangNative.spReflectionTypeLayout_GetFieldByIndex(structLayout, field);
            IntPtr fieldTypeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(fieldLayout);
            int fieldKind = SlangNative.spReflectionTypeLayout_getKind(fieldTypeLayout);
            if (fieldKind == SlangNative.SLANG_TYPE_KIND_STRUCT)
            {
                CollectSamplerSlots(fieldTypeLayout, slots);
                continue;
            }
            if (fieldKind != SlangNative.SLANG_TYPE_KIND_SAMPLER_STATE)
            {
                continue;
            }

            string? fieldName = VariableLayoutName(fieldLayout);
            if (fieldName != null)
            {
                slots.Add(new ShaderSamplerSlot(fieldName, IsComparisonSampler(fieldTypeLayout)));
            }
        }
    }

    /// <summary>
    /// The sampled-texture slots of a block's element struct, in declaration
    /// order, with the shape each declaration requires (the same shape facts
    /// the linked view reports on its bind-group entries). Storage images and
    /// depth textures are excluded — they are pass bindings, not material
    /// texture slots.
    /// </summary>
    private static unsafe void CollectTextureSlots(
        IntPtr structLayout, List<ShaderTextureSlot> slots,
        IReadOnlyDictionary<string, PixelFormat> imageFormats)
    {
        uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(structLayout);
        for (uint field = 0; field < fieldCount; field++)
        {
            IntPtr fieldLayout = SlangNative.spReflectionTypeLayout_GetFieldByIndex(structLayout, field);
            IntPtr fieldTypeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(fieldLayout);
            int fieldKind = SlangNative.spReflectionTypeLayout_getKind(fieldTypeLayout);
            if (fieldKind == SlangNative.SLANG_TYPE_KIND_STRUCT)
            {
                CollectTextureSlots(fieldTypeLayout, slots, imageFormats);
                continue;
            }
            if (fieldKind != SlangNative.SLANG_TYPE_KIND_RESOURCE)
            {
                continue;
            }

            // Storage images are pass outputs, not sampled material slots;
            // depth textures are framebuffer attachments, not material slots.
            IntPtr resourceType = SlangNative.spReflectionTypeLayout_GetType(fieldTypeLayout);
            int shape = SlangNative.spReflectionType_GetResourceShape(resourceType);
            if (SlangNative.spReflectionType_GetResourceAccess(resourceType)
                    != SlangNative.SLANG_RESOURCE_ACCESS_READ
                || (shape & SlangNative.SLANG_TEXTURE_SHADOW_FLAG) != 0)
            {
                continue;
            }

            string? fieldName = VariableLayoutName(fieldLayout);
            if (fieldName == null)
            {
                continue;
            }
            TextureViewDimension dimension = GetViewDimension(shape, fieldName);
            slots.Add(new ShaderTextureSlot(fieldName, dimension,
                GetSampleType(resourceType, fieldName, imageFormats)));
        }
    }
    /// <summary>Every user-defined attribute name of a slang variable, in declaration order.</summary>
    private static unsafe List<string> GetUserAttributeNames(IntPtr variable)
    {
        List<string> names = [];
        if (variable == IntPtr.Zero)
        {
            return names;
        }
        uint count = SlangNative.spReflectionVariable_GetUserAttributeCount(variable);
        for (uint i = 0; i < count; i++)
        {
            IntPtr attribute = SlangNative.spReflectionVariable_GetUserAttribute(variable, i);
            if (attribute == IntPtr.Zero)
            {
                continue;
            }
            string? name = SlangNative.StringFromPtr(
                SlangNative.spReflectionUserAttribute_GetName(attribute));
            if (name != null)
            {
                names.Add(name);
            }
        }
        return names;
    }

    /// <summary>
    /// The scalar/vector float members of a constant-buffer type layout, in
    /// declaration order. Resource members (textures, samplers, storage
    /// buffers) are binding entries, not uniform members, and are skipped;
    /// other non-float members make the method throw.
    /// </summary>
    private static unsafe List<ShaderUniformMember> ReadUniformMembers(IntPtr typeLayout)
    {
        List<ShaderUniformMember> members = ReadUniformMembers(typeLayout, out string? unsupportedReason);
        if (unsupportedReason != null)
        {
            throw new NotSupportedException(unsupportedReason);
        }
        return members;
    }

    /// <summary>
    /// The lenient member walk behind <see cref="ReadUniformMembers(IntPtr)"/>:
    /// the representable members in declaration order plus, through
    /// <paramref name="unsupportedMemberReason"/>, why the first member that
    /// does not fit the uniform view (unsupported scalar width, other kind, a
    /// struct with uniform data, a nested array) is missing — null when the
    /// listing is complete. Resource members (textures, samplers, storage
    /// buffers, and structs that only group them) are binding entries, not
    /// uniform data, and never count as unsupported. Arrays unwrap to one
    /// member with the element's type and the array's element count.
    /// </summary>
    private static unsafe List<ShaderUniformMember> ReadUniformMembers(
        IntPtr typeLayout, out string? unsupportedMemberReason)
    {
        IntPtr structLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
        List<ShaderUniformMember> members = [];
        unsupportedMemberReason = null;
        uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(structLayout);
        for (uint field = 0; field < fieldCount; field++)
        {
            IntPtr fieldLayout = SlangNative.spReflectionTypeLayout_GetFieldByIndex(structLayout, field);
            string? fieldName = VariableLayoutName(fieldLayout);
            if (fieldName == null)
            {
                continue;
            }

            // A block groups uniform data and resource members; the resource
            // members are binding entries, not uniform members. A struct field
            // that carries only resources (e.g. a texture+sampler pair type)
            // is binding entries all the way down — it contributes no uniform
            // data. A struct that DOES carry uniform data stays unsupported:
            // flat members only.
            IntPtr fieldLayoutType = SlangNative.spReflectionVariableLayout_GetTypeLayout(fieldLayout);
            int fieldKind = SlangNative.spReflectionTypeLayout_getKind(fieldLayoutType);
            if (fieldKind == SlangNative.SLANG_TYPE_KIND_RESOURCE ||
                fieldKind == SlangNative.SLANG_TYPE_KIND_SAMPLER_STATE ||
                fieldKind == SlangNative.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER)
            {
                continue;
            }
            if (fieldKind == SlangNative.SLANG_TYPE_KIND_STRUCT)
            {
                uint structUniformSize = (uint)SlangNative.spReflectionTypeLayout_GetSize(
                    fieldLayoutType, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
                if (structUniformSize == 0)
                {
                    continue;
                }

                unsupportedMemberReason ??=
                    $"member '{fieldName}' is a nested struct with uniform data; the uniform view admits flat members only.";
                continue;
            }

            uint offset = (uint)SlangNative.spReflectionVariableLayout_GetOffset(
                fieldLayout, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);

            // An array member unwraps to one entry: the element's type facts
            // plus the array's element count. SizeBytes is the member's full
            // span (stride × count), so SetValues writes the whole array.
            uint elementCount = 1;
            IntPtr elementType = fieldLayoutType;
            if (fieldKind == SlangNative.SLANG_TYPE_KIND_ARRAY)
            {
                IntPtr elementLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(fieldLayoutType);
                if (SlangNative.spReflectionTypeLayout_getKind(elementLayout)
                        == SlangNative.SLANG_TYPE_KIND_ARRAY)
                {
                    unsupportedMemberReason ??=
                        $"member '{fieldName}' is a nested array; the uniform view admits one array level only.";
                    continue;
                }
                elementCount = (uint)SlangNative.spReflectionType_GetElementCount(
                    SlangNative.spReflectionTypeLayout_GetType(fieldLayoutType));
                elementType = elementLayout;
            }

            IntPtr elementTypeRoot = SlangNative.spReflectionTypeLayout_GetType(elementType);
            if (!TryGetMemberType(elementTypeRoot, out int components, out var scalar, out string? reason))
            {
                unsupportedMemberReason ??= reason;
                continue;
            }

            uint size;
            if (elementCount > 1)
            {
                size = (uint)SlangNative.spReflectionTypeLayout_getStride(
                           fieldLayoutType, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM)
                       * elementCount;
            }
            else
            {
                size = (uint)(components * sizeof(float));
            }
            members.Add(new ShaderUniformMember(fieldName, offset, size, components, scalar, elementCount));
        }
        return members;
    }

    /// <summary>The names and stages of every entry point in a program layout.</summary>
    public static unsafe List<(string Name, int Stage)> GetEntryPoints(IntPtr reflection)
    {
        List<(string, int)> result = [];
        nuint count = SlangNative.spReflection_getEntryPointCount(reflection);
        for (nuint i = 0; i < count; i++)
        {
            IntPtr entryPoint = SlangNative.spReflection_getEntryPointByIndex(reflection, i);
            if (entryPoint == IntPtr.Zero)
            {
                continue;
            }
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionEntryPoint_getName(entryPoint));
            if (name != null)
            {
                result.Add((name, SlangNative.spReflectionEntryPoint_getStage(entryPoint)));
            }
        }
        return result;
    }

    /// <summary>
    /// Collects explicitly declared image formats from the binding ranges of the global
    /// parameters layout — the sanctioned cross-target route. The map is keyed
    /// by the leaf variable name of each range.
    /// </summary>
    private static unsafe Dictionary<string, PixelFormat> CollectImageFormats(IntPtr reflection)
    {
        Dictionary<string, PixelFormat> formats = [];
        IntPtr globalLayout = SlangNative.spReflection_getGlobalParamsTypeLayout(reflection);
        if (globalLayout != IntPtr.Zero)
        {
            CollectImageFormatsFromLayout(globalLayout, formats);
        }

        // Storage images declared as block members are not leaves of the global
        // layout's binding ranges (the block itself is); walk each block's
        // element layout so their formats resolve by the same bare member name.
        uint parameterCount = SlangNative.spReflection_GetParameterCount(reflection);
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflection_GetParameterByIndex(reflection, i);
            if (parameter == IntPtr.Zero)
            {
                continue;
            }
            IntPtr typeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(parameter);
            int typeKind = SlangNative.spReflectionTypeLayout_getKind(typeLayout);
            if (typeKind != SlangNative.SLANG_TYPE_KIND_CONSTANT_BUFFER
                && typeKind != SlangNative.SLANG_TYPE_KIND_PARAMETER_BLOCK)
            {
                continue;
            }
            IntPtr elementLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
            if (elementLayout != IntPtr.Zero)
            {
                CollectImageFormatsFromLayout(elementLayout, formats);
            }
        }
        return formats;
    }

    private static unsafe void CollectImageFormatsFromLayout(IntPtr typeLayout, Dictionary<string, PixelFormat> formats)
    {
        int rangeCount = SlangNative.spReflectionTypeLayout_getBindingRangeCount(typeLayout);
        for (int i = 0; i < rangeCount; i++)
        {
            uint rangeType = (uint)SlangNative.spReflectionTypeLayout_getBindingRangeType(typeLayout, i);
            if ((rangeType & SlangNative.SLANG_BINDING_TYPE_BASE_MASK) != SlangNative.SLANG_BINDING_TYPE_TEXTURE)
            {
                continue;
            }
            IntPtr leafVariable = SlangNative.spReflectionTypeLayout_getBindingRangeLeafVariable(typeLayout, i);
            string? name = leafVariable == IntPtr.Zero
                ? null
                : SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(leafVariable));
            if (name == null)
            {
                continue;
            }
            int imageFormat = SlangNative.spReflectionTypeLayout_getBindingRangeImageFormat(typeLayout, i);
            PixelFormat format = ConvertImageFormat(imageFormat);
            if (format != PixelFormat.Undefined)
            {
                formats[name] = format;
            }
        }
    }

    private static ThreadGroupSize ReadThreadGroupSize(IntPtr entryPoint)
    {
        nuint[] axes = new nuint[3];
        SlangNative.spReflectionEntryPoint_getComputeThreadGroupSize(entryPoint, 3, axes);
        return new ThreadGroupSize((uint)axes[0], (uint)axes[1], (uint)axes[2]);
    }

    /// <summary>
    /// The storage-image formats the engine's shaders declare; anything else is
    /// rejected so an unsupported combination surfaces at compile time, not as
    /// a wgpu validation error.
    /// </summary>
    internal static PixelFormat ConvertImageFormat(int slangFormat)
    {
        return slangFormat switch
        {
            SlangNative.SLANG_IMAGE_FORMAT_rgba8 => PixelFormat.RGBA8Unorm,
            SlangNative.SLANG_IMAGE_FORMAT_rgba8_snorm => PixelFormat.RGBA8Snorm,
            SlangNative.SLANG_IMAGE_FORMAT_rgba16f => PixelFormat.RGBA16Float,
            SlangNative.SLANG_IMAGE_FORMAT_rgba32f => PixelFormat.RGBA32Float,
            SlangNative.SLANG_IMAGE_FORMAT_r32f => PixelFormat.R32Float,
            SlangNative.SLANG_IMAGE_FORMAT_r16f => PixelFormat.R16Float,
            SlangNative.SLANG_IMAGE_FORMAT_rg16f => PixelFormat.RG16Float,
            SlangNative.SLANG_IMAGE_FORMAT_r8 => PixelFormat.R8Unorm,
            SlangNative.SLANG_IMAGE_FORMAT_rg8 => PixelFormat.RG8Unorm,
            SlangNative.SLANG_IMAGE_FORMAT_rgba32ui => PixelFormat.RGBA32Uint,
            SlangNative.SLANG_IMAGE_FORMAT_r32ui => PixelFormat.R32Uint,
            SlangNative.SLANG_IMAGE_FORMAT_rg32ui => PixelFormat.RG32Uint,
            _ => PixelFormat.Undefined,
        };
    }

    /// <summary>
    /// Enumerates the resource members (textures, samplers, structured and
    /// storage buffers) of a uniform block declared as
    /// <c>cbuffer name : register(b0, spaceN) { ... }</c>. Members resolve by
    /// their bare field name — the C# contract keeps the flat name the shader
    /// body uses for unqualified member access. A member's binding index and
    /// space are compiler-assigned relative to the block's register
    /// (b0/spaceN), so both must be rebased onto the block's own indices.
    /// A struct-typed field (e.g. a shared texture+sampler pair type) is
    /// legal slang: the compiler flattens its resource leaves into the same
    /// sequential block numbering, and reflection exposes them as
    /// dotted qualified names (`pair.tex`, `pair.samp`).
    /// </summary>
    private static void AddBlockResourceMembers(
        IntPtr parameter,
        IntPtr typeLayout,
        List<(uint Space, BindGroupEntryInfo Entry)> entries,
        Dictionary<string, PixelFormat> imageFormats,
        ShaderStage visibility)
    {
        uint blockSpace = SlangNative.spReflectionParameter_GetBindingSpace(parameter);
        uint blockBinding = SlangNative.spReflectionParameter_GetBindingIndex(parameter);
        IntPtr elementLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
        AddResourceFields(elementLayout, blockSpace, blockBinding, prefix: null, entries, imageFormats, visibility);
    }

    private static void AddResourceFields(
        IntPtr structLayout,
        uint blockSpace,
        uint bindingBase,
        string? prefix,
        List<(uint Space, BindGroupEntryInfo Entry)> entries,
        Dictionary<string, PixelFormat> imageFormats,
        ShaderStage visibility)
    {
        uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(structLayout);
        for (uint field = 0; field < fieldCount; field++)
        {
            IntPtr fieldLayout = SlangNative.spReflectionTypeLayout_GetFieldByIndex(structLayout, field);
            if (fieldLayout == IntPtr.Zero)
            {
                continue;
            }

            string? fieldName = VariableLayoutName(fieldLayout);
            if (fieldName == null)
            {
                continue;
            }

            string qualifiedName = prefix is null ? fieldName : $"{prefix}.{fieldName}";
            IntPtr fieldTypeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(fieldLayout);
            int fieldKind = SlangNative.spReflectionTypeLayout_getKind(fieldTypeLayout);

            if (fieldKind == SlangNative.SLANG_TYPE_KIND_SAMPLER_STATE)
            {
                // Same comparison-type fact as a top-level sampler declaration.
                entries.Add((blockSpace, new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(
                        bindingBase + SlangNative.spReflectionParameter_GetBindingIndex(fieldLayout),
                        visibility,
                        IsComparisonSampler(fieldTypeLayout) ? BindingType.SamplerComparison : BindingType.Sampler,
                        name: qualifiedName),
                }));
                continue;
            }

            if (fieldKind == SlangNative.SLANG_TYPE_KIND_RESOURCE ||
                fieldKind == SlangNative.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER)
            {
                AddResourceEntry(fieldLayout, fieldTypeLayout, fieldKind, qualifiedName, entries, imageFormats, visibility, blockSpace, bindingBase);
                continue;
            }

            if (fieldKind == SlangNative.SLANG_TYPE_KIND_STRUCT)
            {
                // Slang numbers the struct's resource leaves depth-first in
                // declaration order; each nested layout restarts at zero, so
                // the struct field's own start index becomes the new base.
                AddResourceFields(fieldTypeLayout, blockSpace,
                    bindingBase + SlangNative.spReflectionParameter_GetBindingIndex(fieldLayout),
                    qualifiedName, entries, imageFormats, visibility);
                continue;
            }

            // Anything else is ordinary uniform data covered by the block's
            // buffer entry (or nothing, when the block has no uniform data).
        }
    }

    private static void AddResourceEntry(
        IntPtr parameter,
        IntPtr typeLayout,
        int kind,
        string name,
        List<(uint Space, BindGroupEntryInfo Entry)> entries,
        Dictionary<string, PixelFormat> imageFormats,
        ShaderStage visibility,
        uint? spaceOverride = null,
        uint bindingBase = 0)
    {
        IntPtr type = SlangNative.spReflectionTypeLayout_GetType(typeLayout);
        int shape = kind == SlangNative.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER
            ? SlangNative.SLANG_STRUCTURED_BUFFER
            : SlangNative.spReflectionType_GetResourceShape(type);
        int access = SlangNative.spReflectionType_GetResourceAccess(type);
        uint binding = bindingBase + SlangNative.spReflectionParameter_GetBindingIndex(parameter);
        uint space = spaceOverride ?? SlangNative.spReflectionParameter_GetBindingSpace(parameter);

        if (shape == SlangNative.SLANG_STRUCTURED_BUFFER)
        {
            entries.Add((space, new BindGroupEntryInfo
            {
                Entry = new BindGroupEntry(binding, visibility, BindingType.StorageBuffer, name: name),
            }));
            return;
        }

        int baseShape = shape & 0x0F;
        bool isArray = (shape & SlangNative.SLANG_TEXTURE_ARRAY_FLAG) != 0;
        if (isArray)
        {
            throw new NotSupportedException(
                $"Slang parameter '{name}' is a texture array; the reflection bridge does not handle arrays.");
        }

        // DepthTexture* types carry slang's shadow flag and emit a Depth=1
        // OpTypeImage — the sample type must match for wgpu's layout check.
        bool isDepthTexture = (shape & SlangNative.SLANG_TEXTURE_SHADOW_FLAG) != 0;

        TextureViewDimension dimension = GetViewDimension(shape, name);

        if (access == SlangNative.SLANG_RESOURCE_ACCESS_READ_WRITE ||
            access == SlangNative.SLANG_RESOURCE_ACCESS_WRITE)
        {
            if (!imageFormats.TryGetValue(name, out PixelFormat format))
            {
                throw new NotSupportedException(
                    $"Slang storage image '{name}' has no declared image format; storage images must declare "
                    + "one (e.g. [[vk::image_format(rgba16f)]] or a Texture2D<rgba16f> element type).");
            }
            entries.Add((space, new BindGroupEntryInfo
            {
                Entry = new BindGroupEntry(
                    binding,
                    visibility,
                    BindingType.StorageTexture,
                    storageTextureInfo: new StorageTextureBindingInfo(AccessMode.ReadWrite, dimension, format),
                    name: name),
            }));
            return;
        }

        TextureSampleType sampleType = isDepthTexture
            ? TextureSampleType.Depth
            : GetSampleType(type, name, imageFormats);

        entries.Add((space, new BindGroupEntryInfo
        {
            Entry = new BindGroupEntry(
                binding,
                visibility,
                BindingType.Texture,
                new TextureBindingInfo(dimension, sampleType),
                name: name),
        }));
    }

    /// <summary>
    /// Whether a sampler type layout is a <c>SamplerComparisonState</c> (depth
    /// comparison) rather than a plain <c>SamplerState</c> — SPIR-V carries no
    /// comparison marker, so the declared type name is the reflection fact.
    /// The one detection shared by the linked path and the library path.
    /// </summary>
    private static bool IsComparisonSampler(IntPtr samplerTypeLayout)
    {
        IntPtr samplerType = SlangNative.spReflectionTypeLayout_GetType(samplerTypeLayout);
        return SlangNative.StringFromPtr(
            SlangNative.spReflectionType_GetName(samplerType)) == "SamplerComparisonState";
    }

    /// <summary>
    /// The view dimension of a slang resource shape (1D/2D/3D/cube), the one
    /// mapping both the linked path and the library path use — the shadow and
    /// array flags are stripped by the caller.
    /// </summary>
    private static TextureViewDimension GetViewDimension(int shape, string name)
        => (shape & 0x0F) switch
        {
            SlangNative.SLANG_TEXTURE_1D => TextureViewDimension.Texture1D,
            SlangNative.SLANG_TEXTURE_2D => TextureViewDimension.Texture2D,
            SlangNative.SLANG_TEXTURE_3D => TextureViewDimension.Texture3D,
            SlangNative.SLANG_TEXTURE_CUBE => TextureViewDimension.Cube,
            _ => throw new NotSupportedException($"Slang parameter '{name}' has unsupported resource shape {shape & 0x0F}."),
        };

    private static TextureSampleType GetSampleType(
        IntPtr resourceType,
        string name,
        IReadOnlyDictionary<string, PixelFormat> imageFormats)
    {
        if (!imageFormats.TryGetValue(name, out PixelFormat format))
        {
            return TextureSampleType.Float;
        }

        IntPtr resultType = SlangNative.spReflectionType_GetResourceResultType(resourceType);
        int scalarType = resultType == IntPtr.Zero
            ? SlangNative.SLANG_SCALAR_TYPE_NONE
            : SlangNative.spReflectionType_GetScalarType(resultType);

        return format switch
        {
            PixelFormat.R32Float or PixelFormat.RGBA32Float
                when scalarType == SlangNative.SLANG_SCALAR_TYPE_FLOAT32
                => TextureSampleType.UnfilterableFloat,
            PixelFormat.R32Uint or PixelFormat.RGBA32Uint
                => TextureSampleType.Uint,
            _ => TextureSampleType.Float,
        };
    }

    private static IReadOnlyList<BindGroupLayout> GroupBySpace(List<(uint Space, BindGroupEntryInfo Entry)> entries)
    {
        // Group by descriptor space (set), then sort each set by binding index.
        Dictionary<uint, List<BindGroupEntryInfo>> groups = [];
        foreach ((uint space, BindGroupEntryInfo entry) in entries)
        {
            if (!groups.TryGetValue(space, out List<BindGroupEntryInfo>? group))
            {
                group = [];
                groups[space] = group;
            }
            group.Add(entry);
        }

        List<uint> spaces = [.. groups.Keys];
        spaces.Sort();
        List<BindGroupLayout> bindGroups = new(spaces.Count);
        for (int i = 0; i < spaces.Count; i++)
        {
            // The engine requires set indices contiguous from 0.
            if (spaces[i] != (uint)i)
            {
                throw new InvalidOperationException($"Slang assigned a binding to non-contiguous set {spaces[i]}.");
            }
            List<BindGroupEntryInfo> group = groups[spaces[i]];
            group.Sort((a, b) => a.Entry.Binding.CompareTo(b.Entry.Binding));
            // Bindings must be an array: post-processing (depth-texture and
            // comparison-sampler marking) mutates entries in place through the
            // Array pattern match for reflected aggregate resources.
            bindGroups.Add(new BindGroupLayout { Group = (uint)i, Bindings = group.ToArray() });
        }
        return bindGroups;
    }

    private static IReadOnlyList<VertexInputLayout> BuildVertexLayouts(IntPtr reflection)
    {
        IntPtr vertexEntryPoint = FindEntryPoint(reflection, SlangNative.SLANG_STAGE_VERTEX);
        if (vertexEntryPoint == IntPtr.Zero)
        {
            return [];
        }

        // Fields split into two vertex buffers by name convention: a field named
        // "drawData" feeds from the engine's per-draw instance-step buffer (bound
        // at vertex slot 1, fetched with the indirect record's firstInstance as
        // base), every other field feeds from the mesh (vertex slot 0).
        List<VertexElement> elements = [];
        List<VertexElement> drawElements = [];
        uint byteOffset = 0;
        uint drawByteOffset = 0;
        uint parameterCount = SlangNative.spReflectionEntryPoint_getParameterCount(vertexEntryPoint);
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflectionEntryPoint_getParameterByIndex(vertexEntryPoint, i);
            IntPtr typeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(parameter);
            if (SlangNative.spReflectionTypeLayout_getKind(typeLayout) != SlangNative.SLANG_TYPE_KIND_STRUCT)
            {
                continue;
            }

            uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(typeLayout);
            for (uint field = 0; field < fieldCount; field++)
            {
                IntPtr fieldLayout = SlangNative.spReflectionTypeLayout_GetFieldByIndex(typeLayout, field);
                string? semantic = SlangNative.StringFromPtr(
                    SlangNative.spReflectionVariableLayout_GetSemanticName(fieldLayout));
                // System-value semantics (SV_InstanceID...) are builtins, not
                // vertex attributes.
                if (semantic == null || semantic.StartsWith("SV_", StringComparison.Ordinal))
                {
                    continue;
                }

                string name = VariableLayoutName(fieldLayout) ?? semantic;
                uint location = (uint)SlangNative.spReflectionVariableLayout_GetOffset(
                    fieldLayout, SlangNative.SLANG_PARAMETER_CATEGORY_VARYING_INPUT);
                IntPtr fieldType = SlangNative.spReflectionTypeLayout_GetType(
                    SlangNative.spReflectionVariableLayout_GetTypeLayout(fieldLayout));
                VertexFormat format = VertexFormatOf(fieldType);
                if (name == DrawDataFieldName)
                {
                    drawElements.Add(new VertexElement(location, drawByteOffset, format, name));
                    drawByteOffset += FormatSize(format);
                }
                else
                {
                    elements.Add(new VertexElement(location, byteOffset, format, name));
                    byteOffset += FormatSize(format);
                }
            }
        }

        if (elements.Count == 0 && drawElements.Count == 0)
        {
            return [];
        }
        List<VertexInputLayout> layouts = [];
        if (elements.Count > 0)
        {
            layouts.Add(new VertexInputLayout([.. elements], byteOffset, VertexStepMode.Vertex));
        }
        if (drawElements.Count > 0)
        {
            layouts.Add(new VertexInputLayout([.. drawElements], drawByteOffset, VertexStepMode.Instance));
        }
        return layouts;
    }

    /// <summary>
    /// The vertex-input field name convention that routes a field to the
    /// instance-step vertex buffer (vertex slot 1) instead of the mesh buffer
    /// (see <see cref="BuildVertexLayouts"/>).
    /// </summary>
    public const string DrawDataFieldName = "drawData";

    private static uint FormatSize(VertexFormat format)
    {
        return format switch
        {
            VertexFormat.Float32 or VertexFormat.Uint32 or VertexFormat.Sint32 => 4u,
            VertexFormat.Float32x2 or VertexFormat.Uint32x2 or VertexFormat.Sint32x2 => 8u,
            VertexFormat.Float32x3 or VertexFormat.Uint32x3 or VertexFormat.Sint32x3 => 12u,
            VertexFormat.Float32x4 or VertexFormat.Uint32x4 or VertexFormat.Sint32x4 => 16u,
            _ => throw new NotSupportedException($"Vertex format {format} has no known size."),
        };
    }

    private static VertexFormat VertexFormatOf(IntPtr type)
    {
        int kind = SlangNative.spReflectionType_GetKind(type);
        uint columns = kind == SlangNative.SLANG_TYPE_KIND_VECTOR
            ? SlangNative.spReflectionType_GetColumnCount(type)
            : 1u;
        int scalar = SlangNative.spReflectionType_GetScalarType(type);
        return (columns, scalar) switch
        {
            (1, SlangNative.SLANG_SCALAR_TYPE_FLOAT32) => VertexFormat.Float32,
            (2, SlangNative.SLANG_SCALAR_TYPE_FLOAT32) => VertexFormat.Float32x2,
            (3, SlangNative.SLANG_SCALAR_TYPE_FLOAT32) => VertexFormat.Float32x3,
            (4, SlangNative.SLANG_SCALAR_TYPE_FLOAT32) => VertexFormat.Float32x4,
            (1, SlangNative.SLANG_SCALAR_TYPE_UINT32) => VertexFormat.Uint32,
            (2, SlangNative.SLANG_SCALAR_TYPE_UINT32) => VertexFormat.Uint32x2,
            (3, SlangNative.SLANG_SCALAR_TYPE_UINT32) => VertexFormat.Uint32x3,
            (4, SlangNative.SLANG_SCALAR_TYPE_UINT32) => VertexFormat.Uint32x4,
            _ => throw new NotSupportedException(
                $"Vertex attribute type kind {kind} with {columns} components is not supported."),
        };
    }

    private static int CountFragmentOutputs(IntPtr reflection)
    {
        IntPtr fragmentEntryPoint = FindEntryPoint(reflection, SlangNative.SLANG_STAGE_FRAGMENT);
        if (fragmentEntryPoint == IntPtr.Zero)
        {
            return 0;
        }

        // Outputs are SV_TARGETn semantics on out parameters (and on the
        // result var layout when the entry point returns a struct).
        int count = 0;
        uint parameterCount = SlangNative.spReflectionEntryPoint_getParameterCount(fragmentEntryPoint);
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflectionEntryPoint_getParameterByIndex(fragmentEntryPoint, i);
            count = Math.Max(count, TargetIndex(parameter));
        }

        IntPtr result = SlangNative.spReflectionEntryPoint_getResultVarLayout(fragmentEntryPoint);
        if (result != IntPtr.Zero)
        {
            IntPtr resultTypeLayout = SlangNative.spReflectionVariableLayout_GetTypeLayout(result);
            if (SlangNative.spReflectionTypeLayout_getKind(resultTypeLayout) == SlangNative.SLANG_TYPE_KIND_STRUCT)
            {
                uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(resultTypeLayout);
                for (uint field = 0; field < fieldCount; field++)
                {
                    count = Math.Max(count, TargetIndex(
                        SlangNative.spReflectionTypeLayout_GetFieldByIndex(resultTypeLayout, field)));
                }
            }
            else
            {
                count = Math.Max(count, TargetIndex(result));
            }
        }
        return count;
    }

    /// <summary>
    /// The 1-based target count carried by one output: slang canonicalizes the
    /// semantic name to "SV_TARGET" (no index digit) and reports the target
    /// index as the varying-output offset.
    /// </summary>
    private static int TargetIndex(IntPtr variableLayout)
    {
        string? semantic = SlangNative.StringFromPtr(
            SlangNative.spReflectionVariableLayout_GetSemanticName(variableLayout));
        if (semantic == null || !semantic.StartsWith("SV_TARGET", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        int index = (int)SlangNative.spReflectionVariableLayout_GetOffset(
            variableLayout, SlangNative.SLANG_PARAMETER_CATEGORY_VARYING_OUTPUT);
        return index + 1;
    }

    private static IntPtr FindEntryPoint(IntPtr reflection, int stage)
    {
        nuint count = SlangNative.spReflection_getEntryPointCount(reflection);
        for (nuint i = 0; i < count; i++)
        {
            IntPtr entryPoint = SlangNative.spReflection_getEntryPointByIndex(reflection, i);
            if (entryPoint != IntPtr.Zero && SlangNative.spReflectionEntryPoint_getStage(entryPoint) == stage)
            {
                return entryPoint;
            }
        }
        return IntPtr.Zero;
    }

    private static bool IsPushConstant(IntPtr typeLayout)
    {
        // A push-constant cbuffer carries the push-constant parameter
        // category on its type layout; a descriptor-bound cbuffer carries
        // the constant-buffer category instead.
        uint categoryCount = SlangNative.spReflectionTypeLayout_GetCategoryCount(typeLayout);
        for (uint i = 0; i < categoryCount; i++)
        {
            int category = SlangNative.spReflectionTypeLayout_GetCategoryByIndex(typeLayout, i);
            if (category == SlangNative.SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The float component count of a scalar/vector type; matrices report their
    /// total float count (e.g. float4x4 → 16) so uniform harvesting over general
    /// constant buffers (camera matrices) tolerates them.
    /// </summary>
    /// <summary>
    /// The member type facts of a reflection type: the component count (1
    /// scalar, N vector, rows×columns matrix) and the 32-bit scalar type the
    /// CPU marshals by; false leaves the reason the type does not fit the
    /// uniform view (unsupported scalar width, or not a scalar/vector/matrix
    /// kind).
    /// </summary>
    private static bool TryGetMemberType(
        IntPtr type, out int components, out ShaderUniformScalarType scalar, out string? reason)
    {
        components = 0;
        scalar = ShaderUniformScalarType.Float32;
        int kind = SlangNative.spReflectionType_GetKind(type);
        switch (SlangNative.spReflectionType_GetScalarType(type))
        {
            case SlangNative.SLANG_SCALAR_TYPE_FLOAT32:
                scalar = ShaderUniformScalarType.Float32;
                break;
            case SlangNative.SLANG_SCALAR_TYPE_INT32:
                scalar = ShaderUniformScalarType.Int32;
                break;
            case SlangNative.SLANG_SCALAR_TYPE_UINT32:
                scalar = ShaderUniformScalarType.UInt32;
                break;
            case SlangNative.SLANG_SCALAR_TYPE_BOOL:
                scalar = ShaderUniformScalarType.Bool32;
                break;
            default:
                reason = $"a member of unsupported scalar type (the uniform view admits float/int/uint/bool, 32-bit).";
                return false;
        }
        switch (kind)
        {
            case SlangNative.SLANG_TYPE_KIND_SCALAR:
                components = 1;
                reason = null;
                return true;
            case SlangNative.SLANG_TYPE_KIND_VECTOR:
                components = (int)SlangNative.spReflectionType_GetColumnCount(type);
                reason = null;
                return true;
            case SlangNative.SLANG_TYPE_KIND_MATRIX:
                components = (int)(SlangNative.spReflectionType_GetRowCount(type)
                                   * SlangNative.spReflectionType_GetColumnCount(type));
                reason = null;
                return true;
            default:
                reason = "a member that is not a scalar/vector/matrix (flat members only).";
                return false;
        }
    }

    private static unsafe string? VariableLayoutName(IntPtr variableLayout)
    {
        IntPtr variable = SlangNative.spReflectionVariableLayout_GetVariable(variableLayout);
        return variable == IntPtr.Zero
            ? null
            : SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(variable));
    }
}
