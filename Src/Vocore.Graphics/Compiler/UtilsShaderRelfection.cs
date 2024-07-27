using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Reflect;

namespace Vocore.Graphics;

public static class UtilsShaderRelfection
{
    private static readonly Reflect API = Reflect.GetApi();

    public static ShaderReflectionInfo GetSpirvReflection(byte[] vertexSpirv, byte[] fragmentSpirv, bool useStandardStage = false)
    {
        ShaderReflectionInfo vertex = GetSpirvReflection(vertexSpirv, useStandardStage);
        ShaderReflectionInfo fragment = GetSpirvReflection(fragmentSpirv, useStandardStage);


        return MergeReflectionInfo(vertex, fragment);
    }

    public unsafe static ShaderReflectionInfo GetSpirvReflection(byte[] spirv, bool useStandardStage = false)
    {
        ReflectShaderModule module = new ReflectShaderModule();
        fixed (byte* ptr = spirv)
        {
            Result result = API.CreateShaderModule((nuint)spirv.Length, ptr, &module);
            if (result != Result.Success)
            {
                throw new ShaderReflectionException($"Failed to create shader module, result{result}");
            }
        }

        

        ShaderReflectionInfo info = new ShaderReflectionInfo
        {
            BindGroups = GetBindgGroupLayouts(module),
            VertexLayouts = new VertexInputLayout[] { GetVertexInputLayout(module) },
            PushConstantsRanges = GetPushConstants(module),
            Size = GetThreadGroupSize(module)
        };

        API.DestroyShaderModule(&module);

        if (useStandardStage)
        {
            SetToStandardVisibility(info);
        }

        return info;
    }

    public static void SetToStandardVisibility(ShaderReflectionInfo info)
    {
        for (int i = 0; i < info.BindGroups.Length; i++)
        {
            for (int j = 0; j < info.BindGroups[i].Bindings.Length; j++)
            {
                if ((info.BindGroups[i].Bindings[j].Stage & ShaderStage.Vertex) != 0 || (info.BindGroups[i].Bindings[j].Stage & ShaderStage.Fragment) != 0 || (info.BindGroups[i].Bindings[j].Stage & ShaderStage.Compute) != 0)
                {
                    info.BindGroups[i].Bindings[j].Stage = ShaderStage.Standard;
                }
            }
        }
    }

    public unsafe static PushConstantsRange[] GetPushConstants(ReflectShaderModule shaderModule)
    {
        if (shaderModule.PushConstantBlockCount == 0)
        {
            return Array.Empty<PushConstantsRange>();
        }

        ShaderStage stage = UtilsRelfectType.ConvertShaderStage(shaderModule.ShaderStage);
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

        
        PushConstantsRange[] maxRangesList;
        PushConstantsRange[] minRangesList;
        if (vertex.PushConstantsRanges.Length >= fragment.PushConstantsRanges.Length)
        {
            maxRangesList = vertex.PushConstantsRanges;
            minRangesList = fragment.PushConstantsRanges;
        }
        else
        {
            maxRangesList = fragment.PushConstantsRanges;
            minRangesList = vertex.PushConstantsRanges;
        }

        PushConstantsRange[] ranges = new PushConstantsRange[maxRangesList.Length];
        for (int i = 0; i < maxRangesList.Length; i++)
        {
            ranges[i] = maxRangesList[i];
        }

        for (int i = 0; i < minRangesList.Length; i++)
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
        {
            BindGroups = layouts,
            VertexLayouts = vertex.VertexLayouts,
            PushConstantsRanges = ranges,
        };
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

    private unsafe static BindGroupEntry ConvertResourceBinding(DescriptorBinding input, ShaderStage stage)
    {
        BindingType type = UtilsRelfectType.ConvertBindingType(input.DescriptorType);

        TextureBindingInfo? textureBindingInfo = null;
        StorageTextureBindingInfo? storageTextureBindingInfo = null;

        switch (type)
        {
            case BindingType.Texture:
                textureBindingInfo = new TextureBindingInfo(UtilsRelfectType.ConvertTextureViewDimension(input.Image));
                break;
            case BindingType.StorageTexture:
                storageTextureBindingInfo = new StorageTextureBindingInfo(
                    AccessMode.Write,
                    UtilsRelfectType.ConvertTextureViewDimension(input.Image),
                    UtilsRelfectType.ConvertImageFormat(input.Image.ImageFormat)
                    );
                break;
        }

        return new BindGroupEntry(
            input.Binding,
            stage,
            type,
            textureBindingInfo,
            storageTextureBindingInfo,
            UtilsInterop.ReadString(input.Name)
        );
    }

    private unsafe static BindGroupEntry[] GetBindGroups(ReflectDescriptorSet set, ShaderStage stage)
    {
        if (set.BindingCount == 0) return Array.Empty<BindGroupEntry>();

        BindGroupEntry[] bindings = new BindGroupEntry[set.BindingCount];
        for (int i = 0; i < set.BindingCount; i++)
        {
            DescriptorBinding* input = set.Bindings[i];
            bindings[i] = ConvertResourceBinding(*input, stage);
        }

        return bindings;
    }

    private unsafe static BindGroupLayout GetBindgGroupLayout(ReflectDescriptorSet set, ShaderStage stage)
    {
        BindGroupEntry[] bindings = GetBindGroups(set, stage);
        return new BindGroupLayout
        {
            Group = set.Set,
            Bindings = bindings,
        };
    }

    private unsafe static BindGroupLayout[] GetBindgGroupLayouts(ReflectShaderModule shaderModule)
    {
        if (shaderModule.DescriptorSetCount == 0) return Array.Empty<BindGroupLayout>();

        BindGroupLayout[] layouts = new BindGroupLayout[shaderModule.DescriptorSetCount];
        for (int i = 0; i < shaderModule.DescriptorSetCount; i++)
        {
            ReflectDescriptorSet set = shaderModule.DescriptorSets[i];
            layouts[i] = GetBindgGroupLayout(set, UtilsRelfectType.ConvertShaderStage(shaderModule.ShaderStage));
        }

        return layouts;
    }

    private static BindGroupEntry[] MergeBindGroupEntries(params BindGroupEntry[][] bindingsList)
    {
        Dictionary<uint, BindGroupEntry> bindings = new Dictionary<uint, BindGroupEntry>();
        foreach (BindGroupEntry[] list in bindingsList)
        {
            foreach (BindGroupEntry binding in list)
            {
                if (bindings.TryGetValue(binding.Binding, out BindGroupEntry existing))
                {
                    existing.Stage |= binding.Stage;
                    bindings[binding.Binding] = existing;
                }
                else
                {
                    bindings.Add(binding.Binding, binding);
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
            Name = UtilsInterop.ReadString(input.Name),
            Format = UtilsRelfectType.ConvertFormat(input.Format),
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