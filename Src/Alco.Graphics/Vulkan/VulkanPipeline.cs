using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

internal sealed unsafe class VulkanPipeline : GPUPipeline
{
    private VulkanDevice _device;
    private VkPipeline _pipeline;
    private VkPipelineLayout _layout;

    /// <summary>Stage mask used for push constant updates (all stages of this pipeline).</summary>
    public VkShaderStageFlags PushConstantStages { get; }

    /// <summary>Total declared push constant block size.</summary>
    public uint PushConstantsSize { get; }

    public bool IsCompute => IsComputePipeline;

    public VkPipeline Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _pipeline;
    }

    public VkPipelineLayout NativeLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _layout;
    }

    protected override GPUDevice Device => _device;

    private VulkanPipeline(in GraphicsPipelineDescriptor descriptor) : base(descriptor)
    {
        PushConstantStages = CollectStageFlags(descriptor.ShaderModules);
        PushConstantsSize = descriptor.PushConstantsSize;
    }

    private VulkanPipeline(in ComputePipelineDescriptor descriptor) : base(descriptor)
    {
        PushConstantStages = VkShaderStageFlags.Compute;
        PushConstantsSize = descriptor.PushConstantsSize;
    }

    private static VkShaderStageFlags CollectStageFlags(ShaderModule[] modules)
    {
        ShaderStage stages = ShaderStage.None;
        foreach (ShaderModule module in modules)
        {
            stages |= module.Stage;
        }
        return VulkanUtility.ConvertShaderStage(stages);
    }

    public static VulkanPipeline CreateGraphics(VulkanDevice device, in GraphicsPipelineDescriptor descriptor)
    {
        VulkanPipeline pipeline = new(descriptor);
        pipeline._device = device;
        pipeline.CreateGraphicsNative(device, descriptor);
        return pipeline;
    }

    public static VulkanPipeline CreateCompute(VulkanDevice device, in ComputePipelineDescriptor descriptor)
    {
        VulkanPipeline pipeline = new(descriptor);
        pipeline._device = device;
        pipeline.CreateComputeNative(device, descriptor);
        return pipeline;
    }

    private void CreateGraphicsNative(VulkanDevice device, in GraphicsPipelineDescriptor descriptor)
    {
        DescriptorUtility.GetVertexAndPixelModules(descriptor.ShaderModules, out ShaderModule vertex, out ShaderModule pixel);

        VkShaderModule vertexModule = CreateShaderModule(device, vertex, Name);
        VkShaderModule fragmentModule = CreateShaderModule(device, pixel, Name);

        try
        {
            CreatePipelineLayout(device, descriptor.BindGroups);

            // ===== vertex input =====
            VertexInputLayout[] vertexLayouts = descriptor.VertexInputLayouts ?? Array.Empty<VertexInputLayout>();
            int attributeCount = 0;
            foreach (VertexInputLayout layout in vertexLayouts)
            {
                attributeCount += layout.Elements?.Length ?? 0;
            }

            VkVertexInputBindingDescription* bindings = stackalloc VkVertexInputBindingDescription[Math.Max(1, vertexLayouts.Length)];
            VkVertexInputAttributeDescription* attributes = stackalloc VkVertexInputAttributeDescription[Math.Max(1, attributeCount)];

            int bindingCount = 0;
            int attributeIndex = 0;
            foreach (VertexInputLayout layout in vertexLayouts)
            {
                bindings[bindingCount] = new VkVertexInputBindingDescription
                {
                    binding = (uint)bindingCount,
                    stride = layout.Stride,
                    inputRate = VulkanUtility.VertexStepModeToVulkan(layout.StepMode),
                };

                int elementCount = layout.Elements?.Length ?? 0;
                for (int i = 0; i < elementCount; i++)
                {
                    VertexElement element = layout.Elements[i];
                    attributes[attributeIndex++] = new VkVertexInputAttributeDescription
                    {
                        location = element.Location,
                        binding = (uint)bindingCount,
                        offset = element.Offset,
                        format = VulkanUtility.VertexFormatToVulkan(element.Format),
                    };
                }
                bindingCount++;
            }

            VkPipelineVertexInputStateCreateInfo vertexInput = new()
            {
                vertexBindingDescriptionCount = (uint)bindingCount,
                pVertexBindingDescriptions = bindings,
                vertexAttributeDescriptionCount = (uint)attributeIndex,
                pVertexAttributeDescriptions = attributes,
            };

            VkPipelineInputAssemblyStateCreateInfo inputAssembly = new()
            {
                topology = VulkanUtility.PrimitiveTopologyToVulkan(descriptor.PrimitiveTopology),
                primitiveRestartEnable = false,
            };

            // ===== rasterization =====
            RasterizerState rasterizer = descriptor.RasterizerState;
            VkPipelineRasterizationStateCreateInfo rasterization = new()
            {
                polygonMode = VulkanUtility.FillModeToVulkan(rasterizer.FillMode),
                cullMode = VulkanUtility.CullModeToVulkan(rasterizer.CullMode),
                frontFace = VulkanUtility.FrontFaceToVulkan(rasterizer.FrontFace),
                lineWidth = 1.0f,
                depthClampEnable = false,
                rasterizerDiscardEnable = false,
                depthBiasEnable = false,
            };

            // ===== dynamic viewport/scissor/stencil reference =====
            VkDynamicState* dynamicStates = stackalloc VkDynamicState[3]
            {
                VkDynamicState.Viewport,
                VkDynamicState.Scissor,
                VkDynamicState.StencilReference,
            };
            VkPipelineDynamicStateCreateInfo dynamicState = new()
            {
                dynamicStateCount = 3,
                pDynamicStates = dynamicStates,
            };

            VkPipelineViewportStateCreateInfo viewport = new()
            {
                viewportCount = 1,
                scissorCount = 1,
            };

            // ===== depth stencil =====
            DepthStencilState depthStencilState = descriptor.DepthStencilState;
            bool hasDepth = descriptor.DepthStencilFormat.HasValue;
            VkPipelineDepthStencilStateCreateInfo depthStencil = new()
            {
                depthTestEnable = hasDepth && depthStencilState.DepthCompare != CompareFunction.Undefined,
                depthWriteEnable = hasDepth && depthStencilState.DepthWriteEnabled,
                depthCompareOp = VulkanUtility.CompareFunctionToVulkan(depthStencilState.DepthCompare),
                depthBoundsTestEnable = hasDepth && depthStencilState.DepthBoundsTestEnabled && device.Features.DepthBounds,
                stencilTestEnable = hasDepth && (UsesStencil(depthStencilState.FrontFace) || UsesStencil(depthStencilState.BackFace)),
                front = StencilOpState(depthStencilState.FrontFace),
                back = StencilOpState(depthStencilState.BackFace),
                minDepthBounds = 0.0f,
                maxDepthBounds = 1.0f,
            };

            // ===== color blend (one state for every target, WebGPU style) =====
            BlendState blendState = descriptor.BlendState;
            VkPipelineColorBlendAttachmentState blendAttachment = new()
            {
                blendEnable = !IsOpaqueBlend(blendState),
                srcColorBlendFactor = VulkanUtility.BlendFactorToVulkan(blendState.Color.SrcFactor),
                dstColorBlendFactor = VulkanUtility.BlendFactorToVulkan(blendState.Color.DstFactor),
                colorBlendOp = VulkanUtility.BlendOperationToVulkan(blendState.Color.Operation),
                srcAlphaBlendFactor = VulkanUtility.BlendFactorToVulkan(blendState.Alpha.SrcFactor),
                dstAlphaBlendFactor = VulkanUtility.BlendFactorToVulkan(blendState.Alpha.DstFactor),
                alphaBlendOp = VulkanUtility.BlendOperationToVulkan(blendState.Alpha.Operation),
                colorWriteMask = VkColorComponentFlags.R | VkColorComponentFlags.G | VkColorComponentFlags.B | VkColorComponentFlags.A,
            };

            int colorCount = descriptor.ColorFormats?.Length ?? 0;
            VkPipelineColorBlendStateCreateInfo colorBlend = new()
            {
                attachmentCount = (uint)colorCount,
                logicOpEnable = false,
            };
            VkPipelineColorBlendAttachmentState* blendAttachments = stackalloc VkPipelineColorBlendAttachmentState[Math.Max(1, colorCount)];
            for (int i = 0; i < colorCount; i++)
            {
                blendAttachments[i] = blendAttachment;
            }
            colorBlend.pAttachments = blendAttachments;

            // ===== multisample =====
            VkPipelineMultisampleStateCreateInfo multisample = new()
            {
                rasterizationSamples = VkSampleCountFlags.Count1,
                sampleShadingEnable = false,
                minSampleShading = 1.0f,
                alphaToCoverageEnable = false,
                alphaToOneEnable = false,
            };

            // ===== stages =====
            sbyte* vertexEntryPtr = AllocEntryPoint(vertex.EntryPoint);
            sbyte* fragmentEntryPtr = AllocEntryPoint(pixel.EntryPoint);
            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo
            {
                stage = VkShaderStageFlags.Vertex,
                module = vertexModule,
                pName = vertexEntryPtr,
            };
            stages[1] = new VkPipelineShaderStageCreateInfo
            {
                stage = VkShaderStageFlags.Fragment,
                module = fragmentModule,
                pName = fragmentEntryPtr,
            };

            // ===== rendering compatibility (dynamic rendering) =====
            VkFormat* colorFormats = stackalloc VkFormat[Math.Max(1, colorCount)];
            for (int i = 0; i < colorCount; i++)
            {
                colorFormats[i] = VulkanUtility.PixelFormatToVulkan(descriptor.ColorFormats[i]);
            }

            VkFormat depthFormat = VkFormat.Undefined;
            VkFormat stencilFormat = VkFormat.Undefined;
            if (hasDepth)
            {
                VkFormat depthStencilFormat = VulkanUtility.PixelFormatToVulkan(descriptor.DepthStencilFormat.Value);
                if (VulkanUtility.HasStencil(depthStencilFormat))
                {
                    depthFormat = depthStencilFormat;
                    stencilFormat = depthStencilFormat;
                }
                else
                {
                    depthFormat = depthStencilFormat;
                }
            }

            VkPipelineRenderingCreateInfo renderingInfo = new()
            {
                viewMask = 0,
                colorAttachmentCount = (uint)colorCount,
                pColorAttachmentFormats = colorFormats,
                depthAttachmentFormat = depthFormat,
                stencilAttachmentFormat = stencilFormat,
            };

            VkGraphicsPipelineCreateInfo pipelineInfo = new()
            {
                pNext = &renderingInfo,
                stageCount = 2,
                pStages = stages,
                pVertexInputState = &vertexInput,
                pInputAssemblyState = &inputAssembly,
                pViewportState = &viewport,
                pRasterizationState = &rasterization,
                pMultisampleState = &multisample,
                pDepthStencilState = &depthStencil,
                pColorBlendState = &colorBlend,
                pDynamicState = &dynamicState,
                layout = _layout,
            };

            VkPipeline newPipeline = default;
            VkResult result = vkCreateGraphicsPipelines(device.NativeDevice, default, 1, &pipelineInfo, null, &newPipeline);
            VulkanException.ThrowIfFailed(result, $"Failed to create graphics pipeline '{descriptor.Name}'");
            _pipeline = newPipeline;

            NativeMemory.Free(vertexEntryPtr);
            NativeMemory.Free(fragmentEntryPtr);

            device.SetDebugName(VkObjectType.Pipeline, _pipeline.Handle, descriptor.Name);
        }
        finally
        {
            vkDestroyShaderModule(device.NativeDevice, vertexModule, null);
            vkDestroyShaderModule(device.NativeDevice, fragmentModule, null);
        }
    }

    private void CreateComputeNative(VulkanDevice device, in ComputePipelineDescriptor descriptor)
    {
        VkShaderModule computeModule = CreateShaderModule(device, descriptor.Source, Name);
        try
        {
            CreatePipelineLayout(device, descriptor.BindGroups);

            sbyte* entryPtr = AllocEntryPoint(descriptor.Source.EntryPoint);
            VkPipelineShaderStageCreateInfo stage = new()
            {
                stage = VkShaderStageFlags.Compute,
                module = computeModule,
                pName = entryPtr,
            };

            VkComputePipelineCreateInfo pipelineInfo = new()
            {
                stage = stage,
                layout = _layout,
            };

            VkPipeline newPipeline = default;
            VkResult result = vkCreateComputePipelines(device.NativeDevice, default, 1, &pipelineInfo, null, &newPipeline);
            VulkanException.ThrowIfFailed(result, $"Failed to create compute pipeline '{descriptor.Name}'");
            _pipeline = newPipeline;

            NativeMemory.Free(entryPtr);
            device.SetDebugName(VkObjectType.Pipeline, _pipeline.Handle, descriptor.Name);
        }
        finally
        {
            vkDestroyShaderModule(device.NativeDevice, computeModule, null);
        }
    }

    private void CreatePipelineLayout(VulkanDevice device, GPUBindGroup[] bindGroups)
    {
        int layoutCount = bindGroups?.Length ?? 0;
        VkDescriptorSetLayout* setLayouts = stackalloc VkDescriptorSetLayout[Math.Max(1, layoutCount)];
        for (int i = 0; i < layoutCount; i++)
        {
            setLayouts[i] = ((VulkanBindGroup)bindGroups[i]).NativeLayout;
        }

        VkPushConstantRange pushRange = default;
        bool hasPush = PushConstantsSize > 0;
        if (hasPush)
        {
            pushRange = new VkPushConstantRange
            {
                stageFlags = PushConstantStages,
                offset = 0,
                size = VulkanUtility.AlignUp(PushConstantsSize, 4),
            };
        }

        VkPipelineLayoutCreateInfo layoutInfo = new()
        {
            setLayoutCount = (uint)layoutCount,
            pSetLayouts = setLayouts,
            pushConstantRangeCount = hasPush ? 1u : 0u,
            pPushConstantRanges = &pushRange,
        };

        VkPipelineLayout nativeLayout = default;
        VkResult result = vkCreatePipelineLayout(device.NativeDevice, &layoutInfo, null, &nativeLayout);
        _layout = nativeLayout;
        VulkanException.ThrowIfFailed(result, $"Failed to create pipeline layout '{Name}'");
        device.SetDebugName(VkObjectType.PipelineLayout, _layout.Handle, Name + "_layout");
    }

    internal static VkShaderModule CreateShaderModule(VulkanDevice device, in ShaderModule source, string pipelineName)
    {
        if (source.Language != ShaderLanguage.SPIRV)
        {
            throw new GraphicsException(
                $"The Vulkan backend only consumes SPIR-V shaders (pipeline '{pipelineName}' got {source.Language}). " +
                "Select the SPIR-V slang target for the Vulkan backend.");
        }

        if (source.Source.Length == 0)
        {
            throw new GraphicsException($"The shader module for pipeline '{pipelineName}' is empty.");
        }

        if ((source.Source.Length & (sizeof(uint) - 1)) != 0)
        {
            throw new GraphicsException("SPIR-V shader bytecode length must be a multiple of four bytes.");
        }

        ReadOnlySpan<byte> code = source.Source.Span;

        VkShaderModuleCreateInfo createInfo = new()
        {
            codeSize = (nuint)code.Length,
        };

        VkShaderModule module = default;
        fixed (byte* ptr = code)
        {
            createInfo.pCode = (uint*)ptr;
            VkResult result = vkCreateShaderModule(device.NativeDevice, &createInfo, null, &module);
            VulkanException.ThrowIfFailed(result, $"Failed to create SPIR-V shader module for pipeline '{pipelineName}'");
        }

        device.SetDebugName(VkObjectType.ShaderModule, module.Handle, pipelineName + "_shader");
        return module;
    }

    private static sbyte* AllocEntryPoint(string entryPoint)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(entryPoint);
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)(utf8.Length + 1));
        fixed (byte* src = utf8)
        {
            Buffer.MemoryCopy(src, buffer, utf8.Length + 1, utf8.Length);
        }
        buffer[utf8.Length] = 0;
        return (sbyte*)buffer;
    }

    private static bool IsOpaqueBlend(in BlendState state)
    {
        return state.Color.SrcFactor == BlendFactor.One
            && state.Color.DstFactor == BlendFactor.Zero
            && state.Alpha.SrcFactor == BlendFactor.One
            && state.Alpha.DstFactor == BlendFactor.Zero;
    }

    private static bool UsesStencil(in StencilFaceState face)
    {
        return face.Compare != CompareFunction.Always
            || face.PassOperation != StencilOperation.Keep
            || face.DepthFailOperation != StencilOperation.Keep
            || face.StencilFailOperation != StencilOperation.Keep;
    }

    private static VkStencilOpState StencilOpState(in StencilFaceState face)
    {
        return new VkStencilOpState
        {
            failOp = VulkanUtility.StencilOperationToVulkan(face.StencilFailOperation),
            passOp = VulkanUtility.StencilOperationToVulkan(face.PassOperation),
            depthFailOp = VulkanUtility.StencilOperationToVulkan(face.DepthFailOperation),
            compareOp = VulkanUtility.CompareFunctionToVulkan(face.Compare),
            compareMask = 0xFFFFFFFF,
            writeMask = 0xFFFFFFFF,
            reference = 0,
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (_pipeline.Handle != 0)
        {
            _device.QueueNativeDestroy(_pipeline);
            _pipeline = default;
        }
        if (_layout.Handle != 0)
        {
            _device.QueueNativeDestroy(_layout);
            _layout = default;
        }
    }
}
