using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Reflect;

namespace Alco.Graphics;

public static class ShaderRelfectionUtility
{
    private static readonly Reflect API = Reflect.GetApi();

    public static ShaderReflectionInfo GetSpirvReflection(ReadOnlyMemory<byte> vertexSpirv, ReadOnlyMemory<byte> fragmentSpirv, bool useStandardStage = false)
    {
        ShaderReflectionInfo vertex = GetSpirvReflection(vertexSpirv, useStandardStage);
        ShaderReflectionInfo fragment = GetSpirvReflection(fragmentSpirv, useStandardStage);


        return MergeReflectionInfo(vertex, fragment);
    }

    public unsafe static ShaderReflectionInfo GetSpirvReflection(ReadOnlyMemory<byte> spirv, bool useStandardStage = false)
    {
        ReflectShaderModule module = new ReflectShaderModule();
        fixed (byte* ptr = spirv.Span)
        {
            Result result = API.CreateShaderModule((nuint)spirv.Length, ptr, &module);
            if (result != Result.Success)
            {
                throw new ShaderReflectionException($"Failed to create shader module, result{result}");
            }
        }


        ShaderReflectionInfo info = new ShaderReflectionInfo
        (
            new VertexInputLayout[] { GetVertexInputLayout(module) },
            GetBindgGroupLayouts(module,useStandardStage),
            GetPushConstants(module),
            GetThreadGroupSize(module)
        );

        API.DestroyShaderModule(&module);

        // if (useStandardStage)
        // {
        //     SetToStandardVisibility(info);
        // }

        return info;
    }

    // public static void SetToStandardVisibility(ShaderReflectionInfo info)
    // {
    //     for (int i = 0; i < info.BindGroups.Count; i++)
    //     {
    //         for (int j = 0; j < info.BindGroups[i].Bindings.Count; j++)
    //         {
    //             if ((info.BindGroups[i].Bindings[j].Entry.Stage & ShaderStage.Vertex) != 0 ||
    //             (info.BindGroups[i].Bindings[j].Entry.Stage & ShaderStage.Fragment) != 0 ||
    //             (info.BindGroups[i].Bindings[j].Entry.Stage & ShaderStage.Compute) != 0)
    //             {
    //                 info.BindGroups[i].Bindings[j].Entry.Stage = ShaderStage.Standard;
    //             }
    //         }
    //     }
    // }

    public unsafe static PushConstantsRange[] GetPushConstants(ReflectShaderModule shaderModule)
    {
        if (shaderModule.PushConstantBlockCount == 0)
        {
            return Array.Empty<PushConstantsRange>();
        }

        ShaderStage stage = RelfectTypeUtility.ConvertShaderStage(shaderModule.ShaderStage);
        PushConstantsRange[] ranges = new PushConstantsRange[shaderModule.PushConstantBlockCount];
        for (int i = 0; i < shaderModule.PushConstantBlockCount; i++)
        {
            BlockVariable block = shaderModule.PushConstantBlocks[i];
            ranges[i] = new PushConstantsRange
            {
                Stage = stage,
                Start = block.Offset,
                End = block.Offset + block.Size
            };
        }

        return ranges;
    }

    public static ShaderReflectionInfo MergeReflectionInfo(ShaderReflectionInfo vertex, ShaderReflectionInfo fragment)
    {
        Dictionary<uint, BindGroupLayout> bindGroups = new Dictionary<uint, BindGroupLayout>();

        foreach (BindGroupLayout layout in vertex.BindGroups)
        {
            bindGroups.Add(layout.Group, layout);
        }

        foreach (BindGroupLayout layout in fragment.BindGroups)
        {
            if (bindGroups.TryGetValue(layout.Group, out BindGroupLayout existing))
            {
                bindGroups[layout.Group] = new BindGroupLayout
                {
                    Group = layout.Group,
                    Bindings = MergeBindGroupEntries(existing.Bindings, layout.Bindings)
                };
            }
            else
            {
                bindGroups.Add(layout.Group, layout);
            }
        }

        
        IReadOnlyList<PushConstantsRange> maxRangesList;
        IReadOnlyList<PushConstantsRange> minRangesList;
        if (vertex.PushConstantsRanges.Count >= fragment.PushConstantsRanges.Count)
        {
            maxRangesList = vertex.PushConstantsRanges;
            minRangesList = fragment.PushConstantsRanges;
        }
        else
        {
            maxRangesList = fragment.PushConstantsRanges;
            minRangesList = vertex.PushConstantsRanges;
        }

        PushConstantsRange[] ranges = new PushConstantsRange[maxRangesList.Count];
        for (int i = 0; i < maxRangesList.Count; i++)
        {
            ranges[i] = maxRangesList[i];
        }

        for (int i = 0; i < minRangesList.Count; i++)
        {
            ranges[i].Stage |= minRangesList[i].Stage;
        }


        KeyValuePair<uint, BindGroupLayout>[] bindGroupsArray = bindGroups.ToArray();

        Array.Sort(bindGroupsArray, (a, b) =>
        {
            return a.Key.CompareTo(b.Key);
        });

        BindGroupLayout[] layouts = new BindGroupLayout[bindGroupsArray.Length];

        for (int i = 0; i < bindGroupsArray.Length; i++)
        {
            layouts[i] = bindGroupsArray[i].Value;
        }
        

        return new ShaderReflectionInfo
        (
            vertex.VertexLayouts,
            layouts,
            ranges,
            ThreadGroupSize.Default
        );
    }

    // compute thread group size
    private unsafe static ThreadGroupSize GetThreadGroupSize(ReflectShaderModule shaderModule)
    {
        if (shaderModule.EntryPointCount == 0)
        {
            return ThreadGroupSize.Default;
        }

        if ((shaderModule.ShaderStage & ShaderStageFlagBits.ComputeBit) == 0)
        {
            return ThreadGroupSize.Default;
        }

        EntryPoint entry = shaderModule.EntryPoints[0];
        return GetThreadGroupSize(entry);
    }

    private unsafe static ThreadGroupSize GetThreadGroupSize(EntryPoint entry)
    {
        LocalSize size = entry.LocalSize;
        return new ThreadGroupSize
        {
            X = size.X,
            Y = size.Y,
            Z = size.Z
        };
    }


    // resource binding reflection

    private unsafe static BindGroupEntryInfo ConvertResourceBinding(DescriptorBinding input, ShaderStage stage)
    {
        BindingType type = RelfectTypeUtility.ConvertBindingType(input.DescriptorType);

        TextureBindingInfo? textureBindingInfo = null;
        StorageTextureBindingInfo? storageTextureBindingInfo = null;

        switch (type)
        {
            case BindingType.Texture:
                textureBindingInfo = new TextureBindingInfo(RelfectTypeUtility.ConvertTextureViewDimension(input.Image));
                break;
            case BindingType.StorageTexture:
                storageTextureBindingInfo = new StorageTextureBindingInfo(
                    AccessMode.ReadWrite,
                    RelfectTypeUtility.ConvertTextureViewDimension(input.Image),
                    RelfectTypeUtility.ConvertImageFormat(input.Image.ImageFormat)
                    );
                break;
        }

        return new BindGroupEntryInfo
        {
            Entry = new BindGroupEntry(
            input.Binding,
            stage,
            type,
            textureBindingInfo,
            storageTextureBindingInfo,
            InteropUtility.ReadString(input.Name)
        ),
            Size = input.Block.PaddedSize
        };

    }

    private unsafe static BindGroupEntryInfo[] GetBindGroups(ReflectDescriptorSet set, ShaderStage stage)
    {
        if (set.BindingCount == 0) return Array.Empty<BindGroupEntryInfo>();

        BindGroupEntryInfo[] bindings = new BindGroupEntryInfo[set.BindingCount];
        for (int i = 0; i < set.BindingCount; i++)
        {
            DescriptorBinding* input = set.Bindings[i];
            bindings[i] = ConvertResourceBinding(*input, stage);
        }

        return bindings;
    }

    private unsafe static BindGroupLayout GetBindgGroupLayout(ReflectDescriptorSet set, ShaderStage stage)
    {
        BindGroupEntryInfo[] bindings = GetBindGroups(set, stage);
        return new BindGroupLayout
        {
            Group = set.Set,
            Bindings = bindings,
        };
    }

    private unsafe static BindGroupLayout[] GetBindgGroupLayouts(ReflectShaderModule shaderModule, bool useStandardStage = false)
    {
        if (shaderModule.DescriptorSetCount == 0) return Array.Empty<BindGroupLayout>();

        BindGroupLayout[] layouts = new BindGroupLayout[shaderModule.DescriptorSetCount];
        for (int i = 0; i < shaderModule.DescriptorSetCount; i++)
        {
            ReflectDescriptorSet set = shaderModule.DescriptorSets[i];
            ShaderStage stage = RelfectTypeUtility.ConvertShaderStage(shaderModule.ShaderStage);
            if (useStandardStage||
                (stage & ShaderStage.Vertex) != 0 ||
                (stage & ShaderStage.Fragment) != 0 ||
                (stage & ShaderStage.Compute) != 0)
            {
                stage = ShaderStage.Standard;
            }
            layouts[i] = GetBindgGroupLayout(set, stage);
        }

        return layouts;
    }

    private static BindGroupEntryInfo[] MergeBindGroupEntries(params Span<IReadOnlyList<BindGroupEntryInfo>> bindingsList)
    {
        Dictionary<uint, BindGroupEntryInfo> bindings = new Dictionary<uint, BindGroupEntryInfo>();
        foreach (BindGroupEntryInfo[] list in bindingsList)
        {
            foreach (BindGroupEntryInfo binding in list)
            {
                if (bindings.TryGetValue(binding.Entry.Binding, out BindGroupEntryInfo existing))
                {
                    existing.Entry.Stage |= binding.Entry.Stage;
                    bindings[binding.Entry.Binding] = existing;
                }
                else
                {
                    bindings.Add(binding.Entry.Binding, binding);
                }
            }
        }

        return bindings.Values.ToArray();
    }

    // vertex layout reflection

    private unsafe static VertexElement ConvertVertexElement(InterfaceVariable input, uint offset)
    {
        return new VertexElement
        {
            Location = input.Location,
            Name = InteropUtility.ReadString(input.Name),
            Format = RelfectTypeUtility.ConvertFormat(input.Format),
            Offset = offset
        };
    }

    private unsafe static VertexElement[] GetVertexElements(InterfaceVariable** inputs, uint count, out uint stride)
    {
        stride = 0;

        if (count == 0)
        {
            return Array.Empty<VertexElement>();
        }

        List<VertexElement> elements = new List<VertexElement>();
        for (int i = 0; i < count; i++)
        {
            InterfaceVariable* input = inputs[i];
            if (input->BuiltIn >= 0)
            {
                continue;
            }
            elements.Add(ConvertVertexElement(*input, stride));
            stride += GetNumericSize(input->Numeric);
        }

        return elements.ToArray();
    }

    private unsafe static VertexInputLayout GetVertexInputLayout(ReflectShaderModule shaderModule)
    {
        VertexElement[] elements = GetVertexElements(shaderModule.InputVariables, shaderModule.InputVariableCount, out uint stride);
        return new VertexInputLayout(elements, stride, VertexStepMode.Vertex);
    }

    private static uint GetNumericSize(NumericTraits num)
    {
        return num.Scalar.Width / 8 * num.Vector.ComponentCount;
    }

}