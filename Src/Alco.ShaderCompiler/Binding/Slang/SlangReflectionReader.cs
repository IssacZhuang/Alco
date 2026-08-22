using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Translates a slang ProgramLayout (SlangReflection*) into the engine's
// ShaderReflectionInfo, keeping its shape unchanged (plan D4): only the
// producer changes. This is a port of the proven World3D beachhead reader
// with the two SPIR-V fact lookups replaced by first-class slang reflection:
//   - compute thread group size: spReflectionEntryPoint_getComputeThreadGroupSize
//   - storage image formats:     binding-range queries on the global params layout
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One member of a slang uniform block: name, byte offset, size and float
/// component count (1-4 for scalar/vector float members), read from slang's
/// own reflection. Replaces the regex-parsed float4-only material parameter
/// layout of the HLSL surface contract: a block may mix scalar and vector
/// types freely and the C# side writes parameters at the reflected offsets.
/// </summary>
public readonly record struct SlangUniformMember(string Name, uint OffsetBytes, uint SizeBytes, int FloatComponentCount);

public static class SlangReflectionReader
{
    /// <summary>
    /// Builds the engine reflection info (bind groups, vertex layout, push
    /// constants, fragment output count, thread group size) from a slang
    /// program layout. One slang program contains all entry points, so a
    /// single layout covers the whole shader.
    /// </summary>
    public static unsafe ShaderReflectionInfo BuildReflectionInfo(IntPtr reflection)
    {
        List<(uint Space, BindGroupEntryInfo Entry)> entries = [];
        List<PushConstantsRange> pushConstants = [];
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

                uint uniformSize = (uint)SlangNative.spReflectionTypeLayout_GetSize(
                    SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout),
                    SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
                entries.Add((SlangNative.spReflectionParameter_GetBindingSpace(parameter), new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(
                        SlangNative.spReflectionParameter_GetBindingIndex(parameter),
                        visibility,
                        BindingType.UniformBuffer,
                        name: name),
                    Size = uniformSize,
                }));
                continue;
            }

            if (kind == SlangNative.SLANG_TYPE_KIND_SAMPLER_STATE)
            {
                // SamplerComparisonState is a distinct slang type (SPIR-V carries no
                // comparison marker — naga derives it from Dref usage), so the
                // declared type name is the reflection fact.
                IntPtr samplerType = SlangNative.spReflectionTypeLayout_GetType(typeLayout);
                bool comparison = SlangNative.StringFromPtr(
                    SlangNative.spReflectionType_GetName(samplerType)) == "SamplerComparisonState";
                entries.Add((SlangNative.spReflectionParameter_GetBindingSpace(parameter), new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(
                        SlangNative.spReflectionParameter_GetBindingIndex(parameter),
                        visibility,
                        comparison ? BindingType.SamplerComparison : BindingType.Sampler,
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

        IReadOnlyList<BindGroupLayout> bindGroups = GroupBySpace(entries);
        IReadOnlyList<VertexInputLayout> vertexLayouts = BuildVertexLayouts(reflection);
        int fragmentOutputCount = CountFragmentOutputs(reflection);

        return new ShaderReflectionInfo(
            vertexLayouts, bindGroups, pushConstants, threadGroupSize, fragmentOutputCount);
    }

    /// <summary>
    /// The members of a named uniform block (e.g. a surface's
    /// <c>_materialParams</c>), in declaration order, from slang reflection.
    /// Empty when the program declares no such block. Non-float members make
    /// the method throw — the material parameter system writes floats only.
    /// </summary>
    public static unsafe List<SlangUniformMember> GetUniformMembers(IntPtr reflection, string cbufferName)
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
            if (SlangNative.spReflectionTypeLayout_getKind(typeLayout) != SlangNative.SLANG_TYPE_KIND_CONSTANT_BUFFER)
            {
                continue;
            }

            IntPtr structLayout = SlangNative.spReflectionTypeLayout_GetElementTypeLayout(typeLayout);
            List<SlangUniformMember> members = [];
            uint fieldCount = SlangNative.spReflectionTypeLayout_GetFieldCount(structLayout);
            for (uint field = 0; field < fieldCount; field++)
            {
                IntPtr fieldLayout = SlangNative.spReflectionTypeLayout_GetFieldByIndex(structLayout, field);
                string? fieldName = VariableLayoutName(fieldLayout);
                if (fieldName == null)
                {
                    continue;
                }
                uint offset = (uint)SlangNative.spReflectionVariableLayout_GetOffset(
                    fieldLayout, SlangNative.SLANG_PARAMETER_CATEGORY_UNIFORM);
                IntPtr fieldType = SlangNative.spReflectionTypeLayout_GetType(
                    SlangNative.spReflectionVariableLayout_GetTypeLayout(fieldLayout));
                int components = FloatComponents(fieldType);
                uint size = (uint)(components * sizeof(float));
                members.Add(new SlangUniformMember(fieldName, offset, size, components));
            }
            return members;
        }
        return [];
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
        if (globalLayout == IntPtr.Zero)
        {
            return formats;
        }
        int rangeCount = SlangNative.spReflectionTypeLayout_getBindingRangeCount(globalLayout);
        for (int i = 0; i < rangeCount; i++)
        {
            uint rangeType = SlangNative.spReflectionTypeLayout_getBindingRangeType(globalLayout, i);
            if ((rangeType & SlangNative.SLANG_BINDING_TYPE_BASE_MASK) != SlangNative.SLANG_BINDING_TYPE_TEXTURE)
            {
                continue;
            }
            IntPtr leafVariable = SlangNative.spReflectionTypeLayout_getBindingRangeLeafVariable(globalLayout, i);
            string? name = leafVariable == IntPtr.Zero
                ? null
                : SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(leafVariable));
            if (name == null)
            {
                continue;
            }
            int imageFormat = SlangNative.spReflectionTypeLayout_getBindingRangeImageFormat(globalLayout, i);
            PixelFormat format = ConvertImageFormat(imageFormat);
            if (format != PixelFormat.Undefined)
            {
                formats[name] = format;
            }
        }
        return formats;
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
            SlangNative.SLANG_IMAGE_FORMAT_rg16f => PixelFormat.RG16Float,
            SlangNative.SLANG_IMAGE_FORMAT_r8 => PixelFormat.R8Unorm,
            SlangNative.SLANG_IMAGE_FORMAT_rg8 => PixelFormat.RG8Unorm,
            SlangNative.SLANG_IMAGE_FORMAT_rgba32ui => PixelFormat.RGBA32Uint,
            SlangNative.SLANG_IMAGE_FORMAT_r32ui => PixelFormat.R32Uint,
            SlangNative.SLANG_IMAGE_FORMAT_rg32ui => PixelFormat.RG32Uint,
            _ => PixelFormat.Undefined,
        };
    }

    private static void AddResourceEntry(
        IntPtr parameter,
        IntPtr typeLayout,
        int kind,
        string name,
        List<(uint Space, BindGroupEntryInfo Entry)> entries,
        Dictionary<string, PixelFormat> imageFormats,
        ShaderStage visibility)
    {
        IntPtr type = SlangNative.spReflectionTypeLayout_GetType(typeLayout);
        int shape = kind == SlangNative.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER
            ? SlangNative.SLANG_STRUCTURED_BUFFER
            : SlangNative.spReflectionType_GetResourceShape(type);
        int access = SlangNative.spReflectionType_GetResourceAccess(type);
        uint binding = SlangNative.spReflectionParameter_GetBindingIndex(parameter);
        uint space = SlangNative.spReflectionParameter_GetBindingSpace(parameter);

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

        TextureViewDimension dimension = baseShape switch
        {
            SlangNative.SLANG_TEXTURE_1D => TextureViewDimension.Texture1D,
            SlangNative.SLANG_TEXTURE_2D => TextureViewDimension.Texture2D,
            SlangNative.SLANG_TEXTURE_3D => TextureViewDimension.Texture3D,
            SlangNative.SLANG_TEXTURE_CUBE => TextureViewDimension.Cube,
            _ => throw new NotSupportedException($"Slang parameter '{name}' has unsupported resource shape {baseShape}."),
        };

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

        List<VertexElement> elements = [];
        uint byteOffset = 0;
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
                elements.Add(new VertexElement(location, byteOffset, format, name));
                byteOffset += FormatSize(format);
            }
        }

        if (elements.Count == 0)
        {
            return [];
        }
        return [new VertexInputLayout([.. elements], byteOffset, VertexStepMode.Vertex)];
    }

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
    private static int FloatComponents(IntPtr type)
    {
        int kind = SlangNative.spReflectionType_GetKind(type);
        int scalar = SlangNative.spReflectionType_GetScalarType(type);
        if (scalar != SlangNative.SLANG_SCALAR_TYPE_FLOAT32)
        {
            throw new NotSupportedException(
                "Material parameter blocks support float members only (float, float2, float3, float4).");
        }
        return kind switch
        {
            SlangNative.SLANG_TYPE_KIND_SCALAR => 1,
            SlangNative.SLANG_TYPE_KIND_VECTOR => (int)SlangNative.spReflectionType_GetColumnCount(type),
            SlangNative.SLANG_TYPE_KIND_MATRIX => (int)(SlangNative.spReflectionType_GetRowCount(type)
                                                       * SlangNative.spReflectionType_GetColumnCount(type)),
            _ => throw new NotSupportedException(
                $"Material parameter blocks support scalar/vector members only (member kind {kind})."),
        };
    }

    private static unsafe string? VariableLayoutName(IntPtr variableLayout)
    {
        IntPtr variable = SlangNative.spReflectionVariableLayout_GetVariable(variableLayout);
        return variable == IntPtr.Zero
            ? null
            : SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(variable));
    }
}
