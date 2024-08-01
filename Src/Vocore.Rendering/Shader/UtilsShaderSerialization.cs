using Vocore.Graphics;

namespace Vocore.Rendering;

public static class UtilsShaderSerialization
{
    public static byte[] EncodeCompileResult(ShaderCompileResult result)
    {
        BinaryTable table = new BinaryTable
        {
            {"Filename", result.Filename},

            {"VertexShader", EncodeStageSource(result.VertexShader)},
            {"FragmentShader", EncodeStageSource(result.FragmentShader)},
            {"ComputeShader", EncodeStageSource(result.ComputeShader)},

            {"Stages", (int)result.Stages},

            {"RasterizerState", BinaryValue.CreateValueNullable(result.RasterizerState)},
            {"BlendState", BinaryValue.CreateValueNullable(result.BlendState)},
            {"DepthStencilState", BinaryValue.CreateValueNullable(result.DepthStencilState)},
            {"PrimitiveTopology", BinaryValue.CreateValueNullable(result.PrimitiveTopology)},

            {"ReflectionInfo", EncodeReflectionInfo(result.ReflectionInfo)}
        };

        return BinaryParser.EncodeTable(table);
    }

    public static ShaderCompileResult DecodeCompileResult(byte[] bytes)
    {
        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetString("Filename", out string? filename) &&
            table.TryGetBinary("VertexShader", out byte[]? vertexShaderBytes) &&
            table.TryGetBinary("FragmentShader", out byte[]? fragmentShaderBytes) &&
            table.TryGetBinary("ComputeShader", out byte[]? computeShaderBytes) &&
            table.TryGetValue("Stages", out int stages) &&
            table.TryGetNullableValue("RasterizerState", out RasterizerState? rasterizerState) &&
            table.TryGetNullableValue("BlendState", out BlendState? blendState) &&
            table.TryGetNullableValue("DepthStencilState", out DepthStencilState? depthStencilState) &&
            table.TryGetNullableValue("PrimitiveTopology", out PrimitiveTopology? primitiveTopology) &&
           table.TryGetBinary("ReflectionInfo", out byte[]? reflectionInfoTable))
        {
            ShaderModule? vertexShader = DecodeShaderModule(vertexShaderBytes);
            ShaderModule? fragmentShader = DecodeShaderModule(fragmentShaderBytes);
            ShaderModule? computeShader = DecodeShaderModule(computeShaderBytes);

            ShaderStage shaderStage = (ShaderStage)stages;

            ShaderReflectionInfo reflectionInfo = DecodeReflectionInfo(reflectionInfoTable);

            return new ShaderCompileResult(
                filename,
                vertexShader,
                fragmentShader,
                computeShader,
                shaderStage,
                rasterizerState,
                blendState,
                depthStencilState,
                primitiveTopology,
                reflectionInfo
                );
        }

        throw new InvalidOperationException("Invalid shader compile result");

    }

    // // shader preproccess result

    // public static byte[] EncodePreproccessResult(ShaderPreproccessResultHLSL result)
    // {
    //     BinaryArray binaryPragmas = new BinaryArray();
    //     for (int i = 0; i < result.Pragmas.Length; i++)
    //     {
    //         binaryPragmas.Add(result.Pragmas[i].EncodeToBinary());
    //     }
    //     BinaryTable table = new BinaryTable
    //     {
    //         {"ShaderText", result.ShaderText },
    //         {"Filename", result.Filename },
    //         {"Stages", (int)result.Stages},
    //         {"RasterizerState", BinaryValue.CreateValueNullable(result.RasterizerState)},
    //         {"BlendState", BinaryValue.CreateValueNullable(result.BlendState)},
    //         {"DepthStencilState", BinaryValue.CreateValueNullable(result.DepthStencilState)},
    //         {"PrimitiveTopology", BinaryValue.CreateValueNullable(result.PrimitiveTopology)},
    //         {"EntryVertex", result.EntryVertex},
    //         {"EntryFragment", result.EntryFragment},
    //         {"EntryCompute", result.EntryCompute},
    //         {"Pragmas", binaryPragmas },
    //     };

    //     return BinaryParser.EncodeTable(table);
    // }

    // public static ShaderPreproccessResultHLSL DecodePreproccessResult(byte[] bytes)
    // {
    //     BinaryTable table = BinaryParser.DecodeTable(bytes);
    //     if (table.TryGetString("ShaderText", out string? shaderText) &&
    //         table.TryGetString("Filename", out string? filename) &&
    //         table.TryGetValue("Stages", out int stages) &&
    //         table.TryGetString("EntryVertex", out string? entryVertex) &&
    //         table.TryGetString("EntryFragment", out string? entryFragment) &&
    //         table.TryGetString("EntryCompute", out string? entryCompute) &&
    //         //table.TryGetString("RenderPass", out string? renderPass) && removed
    //         table.TryGetNullableValue("RasterizerState", out RasterizerState? rasterizerState) &&
    //         table.TryGetNullableValue("BlendState", out BlendState? blendState) &&
    //         table.TryGetNullableValue("DepthStencilState", out DepthStencilState? depthStencilState) &&
    //         table.TryGetNullableValue("PrimitiveTopology", out PrimitiveTopology? primitiveTopology) &&
    //         table.TryGetArray("Pragmas", out BinaryArray? binaryPragmas))
    //     {
    //         ShaderPragma[] pragmas = new ShaderPragma[binaryPragmas.Count];
    //         for (int i = 0; i < binaryPragmas.Count; i++)
    //         {
    //             if (binaryPragmas.TryGetBinary(i, out byte[]? binaryPragma))
    //             {
    //                 pragmas[i] = ShaderPragma.DecodeFromBinary(binaryPragma);
    //             }
    //         }

    //         ShaderPreproccessResultHLSL result = new ShaderPreproccessResultHLSL
    //         {
    //             ShaderText = shaderText,
    //             Filename = filename,
    //             Stages = (ShaderStage)stages,
    //             EntryVertex = entryVertex,
    //             EntryFragment = entryFragment,
    //             EntryCompute = entryCompute,
    //             RasterizerState = rasterizerState,
    //             PrimitiveTopology = primitiveTopology,
    //             BlendState = blendState,
    //             DepthStencilState = depthStencilState,
    //             Pragmas = pragmas,
    //         };

    //         return result;
    //     }

    //     throw new Exception("Unable to decode ShaderPreproccessResult from binary data.");
    // }

    // shader stage source
    public static byte[] EncodeStageSource(ShaderModule? source)
    {
        if (source.HasValue)
        {
            return EncodeShaderModule(source.Value);
        }
        return Array.Empty<byte>();
    }

    public static byte[] EncodeShaderModule(ShaderModule source)
    {
        BinaryTable table = new BinaryTable
        {
            {"Stage", (int)source.Stage },
            {"Language", (int)source.Language},
            {"Source", source.Source },
            {"EntryPoint", source.EntryPoint },
        };

        return BinaryParser.EncodeTable(table);
    }

    public static ShaderModule? DecodeShaderModule(byte[] bytes)
    {
        if (bytes.Length <= 0)
        {
            return null;
        }

        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetValue("Stage", out int stage) &&
            table.TryGetValue("Language", out int language) &&
            table.TryGetBinary("Source", out byte[]? source) &&
            table.TryGetString("EntryPoint", out string? entryPoint))
        {
            ShaderModule result = new ShaderModule
            {
                Stage = (ShaderStage)stage,
                Language = (ShaderLanguage)language,
                Source = source,
                EntryPoint = entryPoint,
            };

            return result;
        }
        throw new Exception("Unable to decode ShaderStageSource from binary data.");
    }

    // shader reflection info
    //encode
    public static byte[] EncodeReflectionInfo(ShaderReflectionInfo info)
    {
        BinaryArray bindGroups = new BinaryArray();
        for (int i = 0; i < info.BindGroups.Length; i++)
        {
            bindGroups.Add(EncodeBindGroup(info.BindGroups[i]));
        }

        BinaryArray vertexLayouts = new BinaryArray();
        for (int i = 0; i < info.VertexLayouts.Length; i++)
        {
            vertexLayouts.Add(EncodeVertexLayout(info.VertexLayouts[i]));
        }

        BinaryArray pushConstantsRanges = new BinaryArray();
        for (int i = 0; i < info.PushConstantsRanges.Length; i++)
        {
            pushConstantsRanges.Add(BinaryValue.CreateValue(info.PushConstantsRanges[i]));
        }

        BinaryTable table = new BinaryTable
        {
            {"BindGroups", bindGroups },
            {"VertexLayouts", vertexLayouts },
            {"PushConstantsRanges", pushConstantsRanges},
            {"Size", BinaryValue.CreateValue(info.Size)},
        };

        return BinaryParser.EncodeTable(table);
    }

    public static byte[] EncodeBindGroup(BindGroupLayout bindGroup)
    {
        BinaryArray bindings = new BinaryArray();
        for (int i = 0; i < bindGroup.Bindings.Length; i++)
        {
            bindings.Add(EncodeBindEntry(bindGroup.Bindings[i]));
        }
        BinaryTable table = new BinaryTable
        {
            {"Group", bindGroup.Group},
            {"Bindings", bindings },
        };

        return BinaryParser.EncodeTable(table);
    }

    public static byte[] EncodeBindEntry(BindGroupEntry entry)
    {
        BinaryTable table = new BinaryTable
        {
            {"Name", entry.Name },
            {"Type", (int)entry.Type},
            {"Stage", (int)entry.Stage},
            {"Binding", entry.Binding },
            {"TextureInfo", BinaryValue.CreateValue(entry.TextureInfo)},
            {"StorageTextureInfo", BinaryValue.CreateValue(entry.StorageTextureInfo)},
        };

        return BinaryParser.EncodeTable(table);
    }

    public static byte[] EncodeVertexLayout(VertexInputLayout layout)
    {
        BinaryArray elements = new BinaryArray();
        for (int i = 0; i < layout.Elements.Length; i++)
        {
            elements.Add(EncodeVertexElement(layout.Elements[i]));
        }
        BinaryTable table = new BinaryTable
        {
            {"Stride", layout.Stride},
            {"StepMode", (int)layout.StepMode},
            {"Elements", elements },
        };

        return BinaryParser.EncodeTable(table);
    }

    public static byte[] EncodeVertexElement(VertexElement element)
    {
        BinaryTable table = new BinaryTable
        {
            {"Location", element.Location },
            {"Name", element.Name },
            {"Format", (int)element.Format},
            {"Offset", element.Offset },
        };

        return BinaryParser.EncodeTable(table);
    }


    //decode
    public static ShaderReflectionInfo DecodeReflectionInfo(byte[] bytes)
    {
        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetArray("BindGroups", out BinaryArray? binaryBindGroups) &&
            table.TryGetArray("VertexLayouts", out BinaryArray? binaryVertexLayouts) &&
            table.TryGetArray("PushConstantsRanges", out BinaryArray? binaryPushConstantsRanges) &&
            table.TryGetValue("Size", out ThreadGroupSize size))
        {
            BindGroupLayout[] bindGroups = new BindGroupLayout[binaryBindGroups.Count];
            for (int i = 0; i < binaryBindGroups.Count; i++)
            {
                if (binaryBindGroups.TryGetBinary(i, out byte[]? binaryBindGroup))
                {
                    bindGroups[i] = DecodeBindGroup(binaryBindGroup);
                }
            }

            VertexInputLayout[] vertexLayouts = new VertexInputLayout[binaryVertexLayouts.Count];
            for (int i = 0; i < binaryVertexLayouts.Count; i++)
            {
                if (binaryVertexLayouts.TryGetBinary(i, out byte[]? binaryVertexLayout))
                {
                    vertexLayouts[i] = DecodeVertexLayout(binaryVertexLayout);
                }
            }

            PushConstantsRange[] pushConstantsRanges = new PushConstantsRange[binaryPushConstantsRanges.Count];
            for (int i = 0; i < binaryPushConstantsRanges.Count; i++)
            {
                if (binaryPushConstantsRanges.TryGetValue(i, out PushConstantsRange pushConstantsRange))
                {
                    pushConstantsRanges[i] = pushConstantsRange;
                }
            }

            ShaderReflectionInfo result = new ShaderReflectionInfo
            {
                BindGroups = bindGroups,
                VertexLayouts = vertexLayouts,
                PushConstantsRanges = pushConstantsRanges,
                Size = size,
            };

            return result;
        }
        throw new Exception("Unable to decode ShaderReflectionInfo from binary data.");
    }

    public static BindGroupLayout DecodeBindGroup(byte[] bytes)
    {
        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetValue("Group", out uint group) &&
            table.TryGetArray("Bindings", out BinaryArray? binaryBindings))
        {
            BindGroupEntry[] bindings = new BindGroupEntry[binaryBindings.Count];
            for (int i = 0; i < binaryBindings.Count; i++)
            {
                if (binaryBindings.TryGetBinary(i, out byte[]? binaryBindEntry))
                {
                    bindings[i] = DecodeBindEntry(binaryBindEntry);
                }
            }

            BindGroupLayout result = new BindGroupLayout
            {
                Group = group,
                Bindings = bindings,
            };

            return result;
        }
        throw new Exception("Unable to decode BindGroupLayout from binary data.");
    }

    public static BindGroupEntry DecodeBindEntry(byte[] bytes)
    {
        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetString("Name", out string? name) &&
            table.TryGetValue("Type", out int type) &&
            table.TryGetValue("Stage", out int stage) &&
            table.TryGetValue("Binding", out uint binding) &&
            table.TryGetValue("TextureInfo", out TextureBindingInfo textureInfo) &&
            table.TryGetValue("StorageTextureInfo", out StorageTextureBindingInfo storageTextureInfo))
        {
            BindGroupEntry result = new BindGroupEntry
            {
                Name = name,
                Type = (BindingType)type,
                Stage = (ShaderStage)stage,
                Binding = binding,
                TextureInfo = textureInfo,
                StorageTextureInfo = storageTextureInfo,
            };

            return result;
        }
        throw new Exception("Unable to decode BindGroupEntry from binary data.");
    }

    public static VertexInputLayout DecodeVertexLayout(byte[] bytes)
    {
        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetValue("Stride", out uint stride) &&
            table.TryGetValue("StepMode", out int stepMode) &&
            table.TryGetArray("Elements", out BinaryArray? binaryElements))
        {
            VertexElement[] elements = new VertexElement[binaryElements.Count];
            for (int i = 0; i < binaryElements.Count; i++)
            {
                if (binaryElements.TryGetBinary(i, out byte[]? binaryElement))
                {
                    elements[i] = DecodeVertexElement(binaryElement);
                }
            }

            VertexInputLayout result = new VertexInputLayout
            {
                Stride = stride,
                StepMode = (VertexStepMode)stepMode,
                Elements = elements,
            };

            return result;
        }
        throw new Exception("Unable to decode VertexInputLayout from binary data.");
    }

    public static VertexElement DecodeVertexElement(byte[] bytes)
    {
        BinaryTable table = BinaryParser.DecodeTable(bytes);
        if (table.TryGetValue("Location", out uint location) &&
            table.TryGetString("Name", out string? name) &&
            table.TryGetValue("Format", out int format) &&
            table.TryGetValue("Offset", out uint offset))
        {
            VertexElement result = new VertexElement
            {
                Location = location,
                Name = name,
                Format = (VertexFormat)format,
                Offset = offset,
            };

            return result;
        }
        throw new Exception("Unable to decode VertexElement from binary data.");
    }
}