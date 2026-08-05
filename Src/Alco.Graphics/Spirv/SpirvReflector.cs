namespace Alco.Graphics.Spirv;

/// <summary>
/// Reflects a parsed SPIR-V module into engine types (<see cref="ShaderReflectionInfo"/>,
/// <see cref="BindGroupLayout"/>, <see cref="VertexInputLayout"/>, etc.).
/// </summary>
internal static class SpirvReflector
{
    /// <summary>
    /// Reflects SPIR-V bytecode into engine shader reflection info.
    /// </summary>
    public static ShaderReflectionInfo Reflect(ReadOnlySpan<byte> spirv, bool useStandardStage = false)
    {
        SpirvModule module = SpirvReader.Parse(spirv);

        ShaderStage stage = GetShaderStage(module);
        ShaderStage effectiveStage = ResolveEffectiveStage(stage, useStandardStage);

        return new ShaderReflectionInfo(
            [GetVertexInputLayout(module)],
            GetBindGroupLayouts(module, effectiveStage),
            GetPushConstants(module, stage),
            GetThreadGroupSize(module));
    }

    // ─── Shader Stage ───────────────────────────────────────────────

    private static ShaderStage GetShaderStage(SpirvModule module)
    {
        if (module.EntryPoints.Count == 0)
        {
            return ShaderStage.None;
        }

        SpirvInstruction entry = module.EntryPoints[0];
        var model = (SpirvExecutionModel)entry[1];
        return model switch
        {
            SpirvExecutionModel.Vertex => ShaderStage.Vertex,
            SpirvExecutionModel.Fragment => ShaderStage.Fragment,
            SpirvExecutionModel.GLCompute => ShaderStage.Compute,
            SpirvExecutionModel.Geometry => ShaderStage.Geometry,
            SpirvExecutionModel.TessellationControl => ShaderStage.Hull,
            SpirvExecutionModel.TessellationEvaluation => ShaderStage.Domain,
            _ => throw new ShaderReflectionException($"Unsupported execution model {model}")
        };
    }

    private static ShaderStage ResolveEffectiveStage(ShaderStage stage, bool useStandardStage)
    {
        if (useStandardStage ||
            (stage & ShaderStage.Vertex) != 0 ||
            (stage & ShaderStage.Fragment) != 0 ||
            (stage & ShaderStage.Compute) != 0)
        {
            return ShaderStage.Standard;
        }

        return stage;
    }

    // ─── Descriptor Bindings (Bind Groups) ──────────────────────────

    private static BindGroupLayout[] GetBindGroupLayouts(SpirvModule module, ShaderStage stage)
    {
        // Collect all resource variables (those with a DescriptorSet decoration).
        Dictionary<uint, List<BindGroupEntryInfo>> sets = new();

        foreach (SpirvInstruction inst in module.Instructions)
        {
            if ((SpirvOp)inst.OpCode != SpirvOp.Variable)
            {
                continue;
            }

            uint variableId = inst[2];
            if (!module.HasDecoration(variableId, SpirvDecoration.DescriptorSet))
            {
                continue;
            }

            uint set = module.GetDecorationValue(variableId, SpirvDecoration.DescriptorSet);
            BindGroupEntryInfo entry = ConvertResourceBinding(module, inst, stage);

            if (!sets.TryGetValue(set, out List<BindGroupEntryInfo>? list))
            {
                list = new List<BindGroupEntryInfo>();
                sets[set] = list;
            }

            list.Add(entry);
        }

        if (sets.Count == 0)
        {
            return Array.Empty<BindGroupLayout>();
        }

        BindGroupLayout[] layouts = new BindGroupLayout[sets.Count];
        int index = 0;
        foreach (KeyValuePair<uint, List<BindGroupEntryInfo>> pair in sets.OrderBy(p => p.Key))
        {
            layouts[index++] = new BindGroupLayout
            {
                Group = pair.Key,
                Bindings = pair.Value.ToArray()
            };
        }

        return layouts;
    }

    private static BindGroupEntryInfo ConvertResourceBinding(SpirvModule module, SpirvInstruction variable, ShaderStage stage)
    {
        uint variableId = variable[2];
        uint pointerTypeId = variable[1];
        uint binding = module.GetDecorationValue(variableId, SpirvDecoration.Binding);
        string name = module.GetName(variableId) ?? $"unnamed_{binding}";

        SpirvInstruction pointerType = module.GetInstruction(pointerTypeId)
            ?? throw new ShaderReflectionException($"Missing pointer type %{pointerTypeId} for variable %{variableId}.");
        uint pointeeTypeId = pointerType[3];
        SpirvInstruction pointeeType = module.GetInstruction(pointeeTypeId)
            ?? throw new ShaderReflectionException($"Missing pointee type %{pointeeTypeId}.");

        BindingType bindingType;
        TextureBindingInfo? textureInfo = null;
        StorageTextureBindingInfo? storageTextureInfo = null;
        uint size = 0;

        switch ((SpirvOp)pointeeType.OpCode)
        {
            case SpirvOp.TypeImage:
                uint depth = pointeeType[4];
                uint sampled = pointeeType[7];
                if (sampled == 2)
                {
                    // Storage image
                    bindingType = BindingType.StorageTexture;
                    storageTextureInfo = new StorageTextureBindingInfo(
                        AccessMode.ReadWrite,
                        ConvertTextureViewDimension(pointeeType),
                        ConvertImageFormat(pointeeType[8]));
                }
                else
                {
                    // Sampled image (texture)
                    bindingType = BindingType.Texture;
                    TextureSampleType sampleType = depth == 1
                        ? TextureSampleType.Depth
                        : TextureSampleType.Float;
                    textureInfo = new TextureBindingInfo(
                        ConvertTextureViewDimension(pointeeType),
                        sampleType);
                }

                break;

            case SpirvOp.TypeSampler:
                bindingType = BindingType.Sampler;
                break;

            case SpirvOp.TypeSampledImage:
                // Combined image+sampler (rare in DXC output).
                bindingType = BindingType.Texture;
                textureInfo = TextureBindingInfo.Default2D;
                break;

            case SpirvOp.TypeStruct:
                size = ComputeStructSize(module, pointeeTypeId);
                bindingType = module.HasDecoration(pointeeTypeId, SpirvDecoration.BufferBlock)
                    ? BindingType.StorageBuffer
                    : BindingType.UniformBuffer;
                break;

            case SpirvOp.TypeArray:
            case SpirvOp.TypeRuntimeArray:
                // Array wrapping a struct (StructuredBuffer, RWStructuredBuffer, etc.)
                uint elementTypeId = pointeeType[2];
                SpirvInstruction? elementType = module.GetInstruction(elementTypeId);
                if (elementType != null && (SpirvOp)elementType.OpCode == SpirvOp.TypeStruct)
                {
                    size = ComputeStructSize(module, elementTypeId);
                    bindingType = module.HasDecoration(elementTypeId, SpirvDecoration.BufferBlock)
                        ? BindingType.StorageBuffer
                        : BindingType.UniformBuffer;
                }
                else
                {
                    throw new ShaderReflectionException(
                        $"Unsupported descriptor: array of non-struct type (opcode {elementType?.OpCode}).");
                }

                break;

            default:
                throw new ShaderReflectionException(
                    $"Unsupported descriptor type (opcode {(SpirvOp)pointeeType.OpCode}) for variable '{name}'.");
        }

        return new BindGroupEntryInfo
        {
            Entry = new BindGroupEntry(binding, stage, bindingType, textureInfo, storageTextureInfo, name),
            Size = size
        };
    }

    // ─── Vertex Input Layout ────────────────────────────────────────

    private static VertexInputLayout GetVertexInputLayout(SpirvModule module)
    {
        List<VertexElement> elements = new();
        uint stride = 0;

        foreach (SpirvInstruction inst in module.Instructions)
        {
            if ((SpirvOp)inst.OpCode != SpirvOp.Variable)
            {
                continue;
            }

            // OpVariable: Words[1]=PointerType, Words[2]=ResultId, Words[3]=StorageClass
            if ((SpirvStorageClass)inst[3] != SpirvStorageClass.Input)
            {
                continue;
            }

            uint variableId = inst[2];
            if (module.HasDecoration(variableId, SpirvDecoration.BuiltIn))
            {
                continue;
            }

            uint location = module.GetDecorationValue(variableId, SpirvDecoration.Location);
            string name = module.GetName(variableId) ?? $"in{location}";
            VertexFormat format = GetVertexFormat(module, inst[1]);

            elements.Add(new VertexElement(location, stride, format, name));
            stride += GetNumericSize(module, inst[1]);
        }

        return new VertexInputLayout(elements.ToArray(), stride, VertexStepMode.Vertex);
    }

    private static VertexFormat GetVertexFormat(SpirvModule module, uint pointerTypeId)
    {
        SpirvInstruction pointerType = module.GetInstruction(pointerTypeId)!;
        uint typeId = pointerType[3]; // Follow pointer → pointee
        SpirvInstruction type = module.GetInstruction(typeId)!;

        // For vectors, the type is OpTypeVector(elementType, count).
        if ((SpirvOp)type.OpCode == SpirvOp.TypeVector)
        {
            SpirvInstruction elementType = module.GetInstruction(type[2])!;
            uint componentCount = type[3];

            if ((SpirvOp)elementType.OpCode == SpirvOp.TypeFloat)
            {
                return elementType[2] switch
                {
                    32 => componentCount switch
                    {
                        1 => VertexFormat.Float32,
                        2 => VertexFormat.Float32x2,
                        3 => VertexFormat.Float32x3,
                        4 => VertexFormat.Float32x4,
                        _ => VertexFormat.Undefined
                    },
                    16 => componentCount switch
                    {
                        2 => VertexFormat.Float16x2,
                        4 => VertexFormat.Float16x4,
                        _ => VertexFormat.Undefined
                    },
                    _ => VertexFormat.Undefined
                };
            }

            if ((SpirvOp)elementType.OpCode == SpirvOp.TypeInt)
            {
                uint width = elementType[2];
                bool signed = elementType[3] != 0;

                if (width == 32)
                {
                    return (signed, componentCount) switch
                    {
                        (false, 1) => VertexFormat.Uint32,
                        (false, 2) => VertexFormat.Uint32x2,
                        (false, 3) => VertexFormat.Uint32x3,
                        (false, 4) => VertexFormat.Uint32x4,
                        (true, 1) => VertexFormat.Sint32,
                        (true, 2) => VertexFormat.Sint32x2,
                        (true, 3) => VertexFormat.Sint32x3,
                        (true, 4) => VertexFormat.Sint32x4,
                        _ => VertexFormat.Undefined
                    };
                }

                if (width == 16)
                {
                    return (signed, componentCount) switch
                    {
                        (false, 2) => VertexFormat.Uint16x2,
                        (false, 4) => VertexFormat.Uint16x4,
                        (true, 2) => VertexFormat.Sint16x2,
                        (true, 4) => VertexFormat.Sint16x4,
                        _ => VertexFormat.Undefined
                    };
                }
            }
        }

        // Scalar types
        if ((SpirvOp)type.OpCode == SpirvOp.TypeFloat && type[2] == 32)
        {
            return VertexFormat.Float32;
        }

        if ((SpirvOp)type.OpCode == SpirvOp.TypeInt && type[2] == 32)
        {
            return type[3] != 0 ? VertexFormat.Sint32 : VertexFormat.Uint32;
        }

        return VertexFormat.Undefined;
    }

    private static uint GetNumericSize(SpirvModule module, uint pointerTypeId)
    {
        SpirvInstruction pointerType = module.GetInstruction(pointerTypeId)!;
        uint typeId = pointerType[3];
        SpirvInstruction type = module.GetInstruction(typeId)!;

        return (SpirvOp)type.OpCode switch
        {
            SpirvOp.TypeFloat => type[2] / 8,
            SpirvOp.TypeInt => type[2] / 8,
            SpirvOp.TypeVector => GetScalarSize(module, type[2]) * type[3],
            _ => 0
        };
    }

    private static uint GetScalarSize(SpirvModule module, uint typeId)
    {
        SpirvInstruction type = module.GetInstruction(typeId)!;
        return (SpirvOp)type.OpCode switch
        {
            SpirvOp.TypeFloat => type[2] / 8,
            SpirvOp.TypeInt => type[2] / 8,
            _ => 0
        };
    }

    // ─── Push Constants ─────────────────────────────────────────────

    private static PushConstantsRange[] GetPushConstants(SpirvModule module, ShaderStage stage)
    {
        List<PushConstantsRange> ranges = new();

        foreach (SpirvInstruction inst in module.Instructions)
        {
            if ((SpirvOp)inst.OpCode != SpirvOp.Variable)
            {
                continue;
            }

            if ((SpirvStorageClass)inst[3] != SpirvStorageClass.PushConstant)
            {
                continue;
            }

            uint pointerTypeId = inst[1];
            SpirvInstruction pointerType = module.GetInstruction(pointerTypeId)!;
            uint structTypeId = pointerType[3];
            uint size = ComputeStructSize(module, structTypeId);

            ranges.Add(new PushConstantsRange(stage, 0, size));
        }

        return ranges.ToArray();
    }

    // ─── Compute Thread Group Size ──────────────────────────────────

    private static ThreadGroupSize GetThreadGroupSize(SpirvModule module)
    {
        if (module.EntryPoints.Count == 0)
        {
            return ThreadGroupSize.Default;
        }

        SpirvInstruction entry = module.EntryPoints[0];
        if ((SpirvExecutionModel)entry[1] != SpirvExecutionModel.GLCompute)
        {
            return ThreadGroupSize.Default;
        }

        uint entryPointId = entry[2];
        foreach (SpirvInstruction mode in module.ExecutionModes)
        {
            if (mode[1] != entryPointId)
            {
                continue;
            }

            if ((SpirvExecutionMode)mode[2] == SpirvExecutionMode.LocalSize)
            {
                return new ThreadGroupSize(mode[3], mode[4], mode[5]);
            }
        }

        return ThreadGroupSize.Default;
    }

    // ─── Type Size Computation ──────────────────────────────────────

    private static uint ComputeTypeSize(SpirvModule module, uint typeId)
    {
        SpirvInstruction? type = module.GetInstruction(typeId);
        if (type == null)
        {
            return 0;
        }

        switch ((SpirvOp)type.OpCode)
        {
            case SpirvOp.TypeFloat:
            case SpirvOp.TypeInt:
                return type[2] / 8;

            case SpirvOp.TypeVector:
                return ComputeTypeSize(module, type[2]) * type[3];

            case SpirvOp.TypeMatrix:
                return ComputeTypeSize(module, type[2]) * type[3];

            case SpirvOp.TypeArray:
                uint elementSize = ComputeTypeSize(module, type[2]);
                SpirvInstruction? lengthConst = module.GetInstruction(type[3]);
                if (lengthConst != null && (SpirvOp)lengthConst.OpCode == SpirvOp.Constant)
                {
                    return elementSize * lengthConst[3];
                }

                return elementSize;

            case SpirvOp.TypeStruct:
                return ComputeStructSize(module, typeId);

            default:
                return 0;
        }
    }

    private static uint ComputeStructSize(SpirvModule module, uint structTypeId)
    {
        SpirvInstruction structType = module.GetInstruction(structTypeId)!;
        int memberCount = structType.WordCount - 2; // word[0]=header, word[1]=result id

        uint maxSize = 0;
        for (int i = 0; i < memberCount; i++)
        {
            uint memberTypeId = structType[2 + i];
            uint offset = module.GetMemberDecorationValue(structTypeId, (uint)i, SpirvDecoration.Offset);
            uint memberSize = ComputeTypeSize(module, memberTypeId);
            maxSize = Math.Max(maxSize, offset + memberSize);
        }

        return maxSize;
    }

    // ─── Texture Dimension / Format Mapping ─────────────────────────

    private static TextureViewDimension ConvertTextureViewDimension(SpirvInstruction imageType)
    {
        var dim = (SpirvDim)imageType[3];
        uint arrayed = imageType[5];

        return dim switch
        {
            SpirvDim.Dim1D => arrayed > 0 ? TextureViewDimension.Texture1DArray : TextureViewDimension.Texture1D,
            SpirvDim.Dim2D => arrayed > 0 ? TextureViewDimension.Texture2DArray : TextureViewDimension.Texture2D,
            SpirvDim.Dim3D => TextureViewDimension.Texture3D,
            SpirvDim.DimCube => arrayed > 0 ? TextureViewDimension.CubeArray : TextureViewDimension.Cube,
            _ => throw new ShaderReflectionException($"Unsupported texture dimension {dim}")
        };
    }

    private static PixelFormat ConvertImageFormat(uint format)
    {
        return (SpirvImageFormat)format switch
        {
            // 8-bit
            SpirvImageFormat.R8 => PixelFormat.R8Unorm,
            SpirvImageFormat.R8Snorm => PixelFormat.R8Snorm,
            SpirvImageFormat.R8ui => PixelFormat.R8Uint,
            SpirvImageFormat.R8i => PixelFormat.R8Sint,
            // 16-bit
            SpirvImageFormat.R16ui => PixelFormat.R16Uint,
            SpirvImageFormat.R16i => PixelFormat.R16Sint,
            SpirvImageFormat.R16f => PixelFormat.R16Float,
            SpirvImageFormat.Rg8 => PixelFormat.RG8Unorm,
            SpirvImageFormat.Rg8ui => PixelFormat.RG8Uint,
            SpirvImageFormat.Rg8Snorm => PixelFormat.RG8Snorm,
            SpirvImageFormat.Rg8i => PixelFormat.RG8Sint,
            // 32-bit
            SpirvImageFormat.R32f => PixelFormat.R32Float,
            SpirvImageFormat.R32ui => PixelFormat.R32Uint,
            SpirvImageFormat.R32i => PixelFormat.R32Sint,
            SpirvImageFormat.Rg16ui => PixelFormat.RG16Uint,
            SpirvImageFormat.Rg16i => PixelFormat.RG16Sint,
            SpirvImageFormat.Rg16f => PixelFormat.RG16Float,
            SpirvImageFormat.Rgba8 => PixelFormat.RGBA8Unorm,
            SpirvImageFormat.Rgba8Snorm => PixelFormat.RGBA8Snorm,
            SpirvImageFormat.Rgba8ui => PixelFormat.RGBA8Uint,
            SpirvImageFormat.Rgba8i => PixelFormat.RGBA8Sint,
            // Packed 32-bit
            SpirvImageFormat.Rgb10a2ui => PixelFormat.RGB10A2Uint,
            SpirvImageFormat.Rgb10A2 => PixelFormat.RGB10A2Unorm,
            SpirvImageFormat.R11fG11fB10f => PixelFormat.RG11B10Ufloat,
            // 64-bit
            SpirvImageFormat.Rg32f => PixelFormat.RG32Float,
            SpirvImageFormat.Rg32ui => PixelFormat.RG32Uint,
            SpirvImageFormat.Rg32i => PixelFormat.RG32Sint,
            SpirvImageFormat.Rgba16ui => PixelFormat.RGBA16Uint,
            SpirvImageFormat.Rgba16i => PixelFormat.RGBA16Sint,
            SpirvImageFormat.Rgba16f => PixelFormat.RGBA16Float,
            // 128-bit
            SpirvImageFormat.Rgba32f => PixelFormat.RGBA32Float,
            SpirvImageFormat.Rgba32ui => PixelFormat.RGBA32Uint,
            SpirvImageFormat.Rgba32i => PixelFormat.RGBA32Sint,
            SpirvImageFormat.Unknown => PixelFormat.Undefined,
            _ => PixelFormat.Undefined
        };
    }
}
