using Alco.Graphics;
using Alco.Graphics.Spirv;

namespace Alco.World3D;

/// <summary>
/// One member of a Slang uniform block: name, byte offset, size and float
/// component count (1-4 for scalar/vector float members), read from Slang's
/// own reflection. This is what replaces the regex-parsed float4-only
/// <c>_materialParams</c> layout of the HLSL surface contract: a surface may
/// mix scalar and vector types freely and the C# side writes parameters at
/// the reflected offsets.
/// </summary>
public readonly record struct SlangUniformMember(string Name, uint OffsetBytes, uint SizeBytes, int FloatComponentCount);

/// <summary>
/// Translates Slang program reflection into the engine's shader reflection
/// types. One Slang compile contains both entry points, so a single layout
/// covers the program; every binding conservatively gets Vertex|Fragment
/// visibility (an over-approximation WebGPU accepts, matching how the engine
/// merges per-stage DXC reflection).
/// </summary>
internal static class SlangReflection
{
    /// <summary>
    /// Build the engine reflection info (bind groups, vertex layout, push
    /// constants, fragment output count) from a Slang program reflection.
    /// </summary>
    /// <param name="reflection">The Slang reflection (spGetReflection).</param>
    /// <returns>The engine reflection info.</returns>
    public static unsafe ShaderReflectionInfo BuildReflectionInfo(IntPtr reflection)
    {
        return BuildReflectionInfo(reflection, null, null);
    }

    /// <summary>
    /// Build the engine reflection info with the extras Slang reflection cannot
    /// provide for engine pipeline shaders: the compute thread group size and
    /// the pixel format of storage images (both read from the compiled SPIR-V
    /// by the caller - see <see cref="SlangSpirvFacts"/>).
    /// </summary>
    /// <param name="reflection">The Slang reflection (spGetReflection).</param>
    /// <param name="threadGroupSize">The compute thread group size, or null for graphics shaders.</param>
    /// <param name="storageFormatLookup">Resolves a storage-image variable name to its declared pixel format; required when the program declares storage images.</param>
    /// <returns>The engine reflection info.</returns>
    public static unsafe ShaderReflectionInfo BuildReflectionInfo(
        IntPtr reflection,
        ThreadGroupSize? threadGroupSize,
        Func<string, PixelFormat?>? storageFormatLookup)
    {
        List<(uint Space, BindGroupEntryInfo Entry)> entries = [];
        List<PushConstantsRange> pushConstants = [];

        // Every binding conservatively gets the union of the program's entry
        // stages as visibility (an over-approximation WebGPU accepts, matching
        // how the engine merges per-stage DXC reflection). A compute-only
        // program must not claim graphics visibility - the compute pipeline
        // layout would then exclude the compute stage itself.
        ShaderStage visibility = ShaderStage.None;
        if (FindEntryPoint(reflection, SlangNative.SLANG_STAGE_VERTEX) != IntPtr.Zero)
        {
            visibility |= ShaderStage.Vertex;
        }
        if (FindEntryPoint(reflection, SlangNative.SLANG_STAGE_FRAGMENT) != IntPtr.Zero)
        {
            visibility |= ShaderStage.Fragment;
        }
        if (FindEntryPoint(reflection, SlangNative.SLANG_STAGE_COMPUTE) != IntPtr.Zero)
        {
            visibility |= ShaderStage.Compute;
        }
        if (visibility == ShaderStage.None)
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

            string? name = ParameterName(parameter);
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
                entries.Add((SlangNative.spReflectionParameter_GetBindingSpace(parameter), new BindGroupEntryInfo
                {
                    Entry = new BindGroupEntry(
                        SlangNative.spReflectionParameter_GetBindingIndex(parameter),
                        visibility,
                        BindingType.Sampler,
                        name: name),
                }));
                continue;
            }

            if (kind == SlangNative.SLANG_TYPE_KIND_RESOURCE ||
                kind == SlangNative.SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER)
            {
                AddResourceEntry(parameter, typeLayout, kind, name, entries, storageFormatLookup, visibility);
                continue;
            }

            throw new NotSupportedException(
                $"Slang parameter '{name}' has unsupported type kind {kind}; the material bridge handles "
                + "constant buffers, push constants, textures, samplers and structured buffers.");
        }

        IReadOnlyList<BindGroupLayout> bindGroups = GroupBySpace(entries);
        IReadOnlyList<VertexInputLayout> vertexLayouts = BuildVertexLayouts(reflection);
        int fragmentOutputCount = CountFragmentOutputs(reflection);

        return new ShaderReflectionInfo(
            vertexLayouts, bindGroups, pushConstants,
            threadGroupSize ?? ThreadGroupSize.Default, fragmentOutputCount);
    }

    /// <summary>
    /// Apply the engine's depth-texture source conventions to a built
    /// reflection: textures declared with <c>DEFINE_TEX2D_DEPTH(_SAMPLE)</c>
    /// become depth-sample textures and their companion samplers become
    /// comparison samplers. Slang reflection has no notion of depth textures
    /// (neither does DXC - the engine applies the same name-based marking after
    /// its own compiles; the SPIR-V image rewrite itself is shared by both paths in
    /// <see cref="SpirvDepthTexturePatcher"/>).
    /// </summary>
    /// <param name="info">The reflection to patch in place.</param>
    /// <param name="depthTextureNames">Depth texture names from the source macros.</param>
    /// <param name="comparisonSamplerNames">Comparison sampler names (depth texture name + "Sampler").</param>
    public static void MarkDepthTextures(
        ShaderReflectionInfo info,
        IReadOnlyList<string> depthTextureNames,
        IReadOnlyList<string> comparisonSamplerNames)
    {
        foreach (BindGroupLayout layout in info.BindGroups)
        {
            if (layout.Bindings is not BindGroupEntryInfo[] bindings)
            {
                continue;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                ref BindGroupEntryInfo infoEntry = ref bindings[i];
                if (infoEntry.Entry.Type == BindingType.Texture &&
                    depthTextureNames.Contains(infoEntry.Entry.Name))
                {
                    infoEntry.Entry = new BindGroupEntry(
                        infoEntry.Entry.Binding,
                        infoEntry.Entry.Stage,
                        BindingType.Texture,
                        new TextureBindingInfo(
                            infoEntry.Entry.TextureInfo.ViewDimension,
                            TextureSampleType.Depth),
                        name: infoEntry.Entry.Name);
                }
                else if (infoEntry.Entry.Type == BindingType.Sampler &&
                    comparisonSamplerNames.Contains(infoEntry.Entry.Name))
                {
                    infoEntry.Entry = new BindGroupEntry(
                        infoEntry.Entry.Binding,
                        infoEntry.Entry.Stage,
                        BindingType.SamplerComparison,
                        name: infoEntry.Entry.Name);
                }
            }
        }
    }

    /// <summary>
    /// The members of a named uniform block (e.g. a surface's
    /// <c>_materialParams</c>), in declaration order, from Slang reflection.
    /// Empty when the program declares no such block. Non-float members make
    /// the method throw - the material parameter system writes floats only.
    /// </summary>
    /// <param name="reflection">The Slang reflection.</param>
    /// <param name="cbufferName">The uniform block name.</param>
    /// <returns>The block's members; empty when absent.</returns>
    public static unsafe List<SlangUniformMember> GetUniformMembers(IntPtr reflection, string cbufferName)
    {
        uint parameterCount = SlangNative.spReflection_GetParameterCount(reflection);
        for (uint i = 0; i < parameterCount; i++)
        {
            IntPtr parameter = SlangNative.spReflection_GetParameterByIndex(reflection, i);
            if (parameter == IntPtr.Zero || ParameterName(parameter) != cbufferName)
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

    private static void AddResourceEntry(
        IntPtr parameter,
        IntPtr typeLayout,
        int kind,
        string name,
        List<(uint Space, BindGroupEntryInfo Entry)> entries,
        Func<string, PixelFormat?>? storageFormatLookup,
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
            PixelFormat? format = storageFormatLookup?.Invoke(name)
                ?? throw new NotSupportedException(
                    $"Slang storage image '{name}' has no declared image format in the compiled SPIR-V; "
                    + "the engine's DEFINE_TEX*_STORAGE macros declare one via [[vk::image_format]].");
            entries.Add((space, new BindGroupEntryInfo
            {
                Entry = new BindGroupEntry(
                    binding,
                    visibility,
                    BindingType.StorageTexture,
                    storageTextureInfo: new StorageTextureBindingInfo(AccessMode.ReadWrite, dimension, format.Value),
                    name: name),
            }));
            return;
        }

        entries.Add((space, new BindGroupEntryInfo
        {
            Entry = new BindGroupEntry(
                binding,
                visibility,
                BindingType.Texture,
                new TextureBindingInfo(dimension, TextureSampleType.Float),
                name: name),
        }));
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
            // array pattern match, mirroring the engine's DXC reflection.
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
    /// The 1-based target count carried by one output: Slang canonicalizes the
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

    /// <summary>The float component count (1-4) of a scalar/vector float type.</summary>
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
            _ => throw new NotSupportedException(
                $"Material parameter blocks support scalar/vector members only (member kind {kind})."),
        };
    }

    private static unsafe string? ParameterName(IntPtr parameter)
    {
        return VariableLayoutName(parameter);
    }

    private static unsafe string? VariableLayoutName(IntPtr variableLayout)
    {
        IntPtr variable = SlangNative.spReflectionVariableLayout_GetVariable(variableLayout);
        return variable == IntPtr.Zero
            ? null
            : SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(variable));
    }
}
