using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Reflect;

namespace Vocore.Graphics;

public static class UtilsShaderRelfection
{
    private static readonly Reflect API = Reflect.GetApi();

    public static ShaderReflectionInfo GetSpirvReflection(byte[] vertexSpirv, byte[] fragmentSpirv)
    {
        ShaderReflectionInfo vertex = GetSpirvReflection(vertexSpirv);
        ShaderReflectionInfo fragment = GetSpirvReflection(fragmentSpirv);
        return MergeReflectionInfo(vertex, fragment);
    }

    public unsafe static ShaderReflectionInfo GetSpirvReflection(byte[] spirv)
    {
        ReflectShaderModule module = new ReflectShaderModule();
        fixed (byte* ptr = spirv)
        {
            Result result = API.CreateShaderModule((nuint)spirv.Length, ptr, &module);
            if (result != Result.Success)
            {
                throw new ShaderCompilationException("Failed to create shader module");
            }
        }

        //TODO: implement reflection info
        ShaderReflectionInfo info = new ShaderReflectionInfo
        {
            BindGroups = GetBindgGroupLayouts(module),
            VertexLayouts = new VertexInputLayout[] { GetVertexInputLayout(module) },
            Size = GetThreadGroupSize(module)
        };

        API.DestroyShaderModule(&module);

        return info;
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

        return new ShaderReflectionInfo
        {
            BindGroups = bindGroups.Values.ToArray(),
            VertexLayouts = vertex.VertexLayouts
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