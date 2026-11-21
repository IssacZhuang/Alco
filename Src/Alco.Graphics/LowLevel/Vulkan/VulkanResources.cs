// Vulkan resources, descriptor heaps, pipelines, and command pools.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Vk = Vulkan;
using static global::Alco.Graphics.LowLevel.VulkanBackend;
using static Vulkan.Vulkan;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal static unsafe partial class VulkanBackend
{
    // Carries the inline view-format list and image create info built by prepareTexture; the
    // caller pins ViewFormats and wires pViewFormats/pNext/pQueueFamilyIndices before use.
    internal struct PreparedTexture
    {
        public Vk.VkFormat[] ViewFormats;
        public Vk.VkImageFormatListCreateInfo FormatList;
        public Vk.VkImageCreateInfo ImageInfo;
    }

    internal static void PrepareTexture(VulkanDeviceBase device, in TextureDesc desc, ref PreparedTexture output)
    {
        output = default;
        output.ViewFormats = new Vk.VkFormat[Backend.FormatCount];
        output.FormatList = new Vk.VkImageFormatListCreateInfo();
        output.ImageInfo = new Vk.VkImageCreateInfo();

        Vk.VkImageUsageFlags usage = 0;
        if (Backend.HasFlag(desc.Usage, TextureUsage.Sampled)) usage |= Vk.VkImageUsageFlags.Sampled;
        if (Backend.HasFlag(desc.Usage, TextureUsage.Storage)) usage |= Vk.VkImageUsageFlags.Storage;
        if (Backend.HasFlag(desc.Usage, TextureUsage.ColorAttachment)) usage |= Vk.VkImageUsageFlags.ColorAttachment;
        if (Backend.HasFlag(desc.Usage, TextureUsage.DepthStencilAttachment)) usage |= Vk.VkImageUsageFlags.DepthStencilAttachment;
        if (Backend.HasFlag(desc.Usage, TextureUsage.TransferSource)) usage |= Vk.VkImageUsageFlags.TransferSrc;
        if (Backend.HasFlag(desc.Usage, TextureUsage.TransferDestination)) usage |= Vk.VkImageUsageFlags.TransferDst;

        Vk.VkImageType imageType = Backend.ToVk(desc.Type);
        Vk.VkFormat format = Backend.ToVk(desc.Format);
        Vk.VkFormatFeatureFlags2 sampledFeatures = Backend.RequiredFormatFeatures(TextureUsage.Sampled);
        Vk.VkFormatFeatureFlags2 storageFeatures = Backend.RequiredFormatFeatures(TextureUsage.Storage);
        uint viewFormatCount = 1;
        output.ViewFormats[0] = format;
        for (uint value = 0; desc.MutableFormat && value < Backend.FormatCount; ++value)
        {
            Format viewFormat = (Format)value;
            if (viewFormat == desc.Format || !Backend.CompatibleViewFormats(desc.Format, viewFormat))
                continue;
            Vk.VkFormatFeatureFlags2 viewFeatures = device.FormatFeatures[(int)value];
            bool sampled = Backend.HasFlag(desc.Usage, TextureUsage.Sampled) && (viewFeatures & sampledFeatures) == sampledFeatures;
            bool storage = Backend.HasFlag(desc.Usage, TextureUsage.Storage) && (viewFeatures & storageFeatures) == storageFeatures;
            if (!sampled && !storage)
                continue;
            Debug.Assert(viewFormatCount < Backend.FormatCount);
            output.ViewFormats[viewFormatCount++] = Backend.ToVk(viewFormat);
        }

        Vk.VkImageCreateFlags imageFlags = 0;
        if (desc.Type == TextureType.Cube || desc.Type == TextureType.CubeArray) imageFlags |= Vk.VkImageCreateFlags.CubeCompatible;
        if (viewFormatCount > 1) imageFlags |= Vk.VkImageCreateFlags.MutableFormat;

        output.FormatList.viewFormatCount = viewFormatCount;
        output.ImageInfo = new Vk.VkImageCreateInfo
        {
            flags = imageFlags,
            imageType = imageType,
            format = format,
            extent = new Vk.VkExtent3D(desc.Extent.X, desc.Extent.Y, desc.Extent.Z),
            mipLevels = desc.MipLevels,
            arrayLayers = desc.LayerCount,
            samples = (Vk.VkSampleCountFlags)desc.SampleCount,
            tiling = Vk.VkImageTiling.Optimal,
            usage = usage,
            sharingMode = device.QueueFamilyCount > 1 ? Vk.VkSharingMode.Concurrent : Vk.VkSharingMode.Exclusive,
            queueFamilyIndexCount = device.QueueFamilyCount,
            initialLayout = Vk.VkImageLayout.Undefined,
        };
    }

    internal static Vk.VkImageViewCreateInfo TextureDescriptorView(VulkanTexture texture, TextureDescriptorType type, TextureDescriptorDesc desc,
        Vk.VkImageViewUsageCreateInfo* usage)
    {
        Vk.VkImageUsageFlags descriptorUsage = type == TextureDescriptorType.Sampled
            ? Vk.VkImageUsageFlags.Sampled
            : Vk.VkImageUsageFlags.Storage;

        Vk.VkImageAspectFlags descriptorAspect = 0;
        switch (desc.Aspect)
        {
            case TextureAspect.Automatic:
                descriptorAspect = Backend.HasDepthAspect(texture.Format) ? Vk.VkImageAspectFlags.Depth :
                                    Backend.HasStencilAspect(texture.Format) ? Vk.VkImageAspectFlags.Stencil : Vk.VkImageAspectFlags.Color;
                break;
            case TextureAspect.Color: descriptorAspect = Vk.VkImageAspectFlags.Color; break;
            case TextureAspect.Depth: descriptorAspect = Vk.VkImageAspectFlags.Depth; break;
            case TextureAspect.Stencil: descriptorAspect = Vk.VkImageAspectFlags.Stencil; break;
        }
        *usage = new Vk.VkImageViewUsageCreateInfo
        {
            usage = descriptorUsage,
        };
        return new Vk.VkImageViewCreateInfo
        {
            pNext = usage,
            image = texture.Image,
            viewType = Backend.ToVkView(texture.Type),
            format = Backend.ToVk(desc.Format == Format.Undefined ? texture.Format : desc.Format),
            subresourceRange = new Vk.VkImageSubresourceRange(descriptorAspect, desc.BaseMip,
                desc.MipCount == 0 ? VK_REMAINING_MIP_LEVELS : desc.MipCount, desc.BaseLayer,
                desc.LayerCount == 0 ? VK_REMAINING_ARRAY_LAYERS : desc.LayerCount),
        };
    }

    internal static Vk.VkSamplerCreateInfo SamplerInfo(SamplerDesc desc)
    {
        return new Vk.VkSamplerCreateInfo
        {
            magFilter = (Vk.VkFilter)desc.MagFilter,
            minFilter = (Vk.VkFilter)desc.MinFilter,
            mipmapMode = (Vk.VkSamplerMipmapMode)desc.MipFilter,
            addressModeU = (Vk.VkSamplerAddressMode)desc.AddressU,
            addressModeV = (Vk.VkSamplerAddressMode)desc.AddressV,
            addressModeW = (Vk.VkSamplerAddressMode)desc.AddressW,
            anisotropyEnable = desc.Anisotropic,
            maxAnisotropy = desc.Anisotropic ? 4.0f : 1.0f,
            compareEnable = desc.CompareEnabled,
            compareOp = (Vk.VkCompareOp)desc.Compare,
            maxLod = VK_LOD_CLAMP_NONE,
        };
    }

    internal static void AssertSpirv(ReadOnlySpan<byte> code)
    {
            Debug.Assert(!code.IsEmpty && code.Length >= 5 * sizeof(uint) && code.Length % sizeof(uint) == 0,
                     "GPUPipeline stages require aligned SPIR-V binaries");
        fixed (byte* pointer = code)
        {
            Debug.Assert((nint)pointer % sizeof(uint) == 0 &&
                         *(uint*)pointer == 0x07230203u, "GPUPipeline stage does not contain SPIR-V");
        }
    }

    internal static VulkanPso CreateRasterPso(VulkanDeviceBase device, Vk.VkPipelineShaderStageCreateInfo* stages, uint stageCount,
        Vk.VkPipelineCreateFlags2CreateInfo* flagsInfo, ReadOnlySpan<ColorTargetDesc> ColorTargets, Format DepthFormat, Format StencilFormat,
        RasterizationState rasterizationState, PrimitiveTopology topology, bool mesh, uint sampleCount)
    {
        Backend.RequireRasterSamples(sampleCount, ColorTargets, DepthFormat, StencilFormat);
        Debug.Assert(ColorTargets.Length <= Backend.MaxColorAttachments);

        Vk.VkPipelineVertexInputStateCreateInfo vertexInput = new();
        Vk.VkPipelineInputAssemblyStateCreateInfo inputAssembly = new()
        {
            topology = Backend.ToVk(topology),
            primitiveRestartEnable = topology is PrimitiveTopology.LineStrip or PrimitiveTopology.TriangleStrip,
        };
        Vk.VkPipelineViewportStateCreateInfo viewportState = new();
        Vk.VkPipelineRasterizationStateCreateInfo rasterization = new()
        {
            polygonMode = rasterizationState.Fill == FillMode.Wireframe ? Vk.VkPolygonMode.Line : Vk.VkPolygonMode.Fill,
            cullMode = rasterizationState.Cull switch
            {
                CullMode.Clockwise => Vk.VkCullModeFlags.Back,
                CullMode.CounterClockwise => Vk.VkCullModeFlags.Front,
                _ => Vk.VkCullModeFlags.None,
            },
            frontFace = Vk.VkFrontFace.CounterClockwise,
            depthBiasEnable = rasterizationState.DepthBiasConstant != 0.0f ||
                               rasterizationState.DepthBiasClamp != 0.0f ||
                               rasterizationState.DepthBiasSlope != 0.0f,
            depthBiasConstantFactor = rasterizationState.DepthBiasConstant,
            depthBiasClamp = rasterizationState.DepthBiasClamp,
            depthBiasSlopeFactor = rasterizationState.DepthBiasSlope,
            lineWidth = 1.0f,
        };
        Vk.VkPipelineMultisampleStateCreateInfo multisample = new()
        {
            rasterizationSamples = (Vk.VkSampleCountFlags)sampleCount,
        };
        Vk.VkPipelineDepthStencilStateCreateInfo depthStencil = new();
        Vk.VkPipelineColorBlendAttachmentState* colorAttachments = stackalloc Vk.VkPipelineColorBlendAttachmentState[(int)Backend.MaxColorAttachments];
        for (int index = 0; index < ColorTargets.Length; ++index)
        {
            ColorTargetDesc target = ColorTargets[index];
            colorAttachments[index] = new Vk.VkPipelineColorBlendAttachmentState
            {
                blendEnable = target.Blend.Enabled,
                srcColorBlendFactor = Backend.ToVk(target.Blend.Color.Source),
                dstColorBlendFactor = Backend.ToVk(target.Blend.Color.Destination),
                colorBlendOp = (Vk.VkBlendOp)target.Blend.Color.Operation,
                srcAlphaBlendFactor = Backend.ToVk(target.Blend.Alpha.Source),
                dstAlphaBlendFactor = Backend.ToVk(target.Blend.Alpha.Destination),
                alphaBlendOp = (Vk.VkBlendOp)target.Blend.Alpha.Operation,
                colorWriteMask = (Vk.VkColorComponentFlags)target.WriteMask,
            };
        }
        Vk.VkPipelineColorBlendStateCreateInfo colorBlend = new()
        {
            attachmentCount = (uint)ColorTargets.Length,
            pAttachments = ColorTargets.Length != 0 ? colorAttachments : null,
        };
        Vk.VkDynamicState* dynamicStates = stackalloc Vk.VkDynamicState[10]
        {
            Vk.VkDynamicState.ViewportWithCount,
            Vk.VkDynamicState.ScissorWithCount,
            Vk.VkDynamicState.DepthTestEnable,
            Vk.VkDynamicState.DepthWriteEnable,
            Vk.VkDynamicState.DepthCompareOp,
            Vk.VkDynamicState.StencilTestEnable,
            Vk.VkDynamicState.StencilOp,
            Vk.VkDynamicState.StencilCompareMask,
            Vk.VkDynamicState.StencilWriteMask,
            Vk.VkDynamicState.StencilReference,
        };
        Vk.VkPipelineDynamicStateCreateInfo dynamicState = new()
        {
            dynamicStateCount = 10,
            pDynamicStates = dynamicStates,
        };
        Vk.VkFormat* colorFormats = stackalloc Vk.VkFormat[(int)Backend.MaxColorAttachments];
        for (int index = 0; index < ColorTargets.Length; ++index)
        {
            colorFormats[index] = Backend.ToVk(ColorTargets[index].Format);
        }
        Vk.VkPipelineRenderingCreateInfo renderingInfo = new()
        {
            colorAttachmentCount = (uint)ColorTargets.Length,
            pColorAttachmentFormats = ColorTargets.Length != 0 ? colorFormats : null,
            depthAttachmentFormat = Backend.ToVk(DepthFormat),
            stencilAttachmentFormat = Backend.ToVk(StencilFormat),
        };
        if (flagsInfo != null) flagsInfo->pNext = &renderingInfo;
        Vk.VkGraphicsPipelineCreateInfo psoInfo = new()
        {
            pNext = flagsInfo != null ? (void*)flagsInfo : &renderingInfo,
            stageCount = stageCount,
            pStages = stages,
            pVertexInputState = mesh ? null : &vertexInput,
            pInputAssemblyState = mesh ? null : &inputAssembly,
            pViewportState = &viewportState,
            pRasterizationState = &rasterization,
            pMultisampleState = &multisample,
            pDepthStencilState = &depthStencil,
            pColorBlendState = &colorBlend,
            pDynamicState = &dynamicState,
            layout = device.ShaderLayout,
            basePipelineIndex = -1,
        };
        VulkanPso result = new()
        {
            Owner = device,
            BindPoint = Vk.VkPipelineBindPoint.Graphics,
        };
        Vk.VkPipeline pipeline = default;
        try
        {
            Backend.CheckVk(device.Api.vkCreateGraphicsPipelines(device.PipelineCache, 1, &psoInfo, null, &pipeline), "vkCreateGraphicsPipelines");
            if (!device.PipelineCache.IsNull) Volatile.Write(ref device.PipelineCacheDirty, true);
            result.Pipeline = pipeline;
            pipeline = default;
            return result;
        }
        finally
        {
            if (!pipeline.IsNull) device.Api.vkDestroyPipeline(pipeline, null);
        }
    }
}

internal abstract unsafe partial class VulkanDeviceBase
{
    internal Vk.VkPipelineLayout ShaderLayout;

    /// <summary>Allocates a dedicated texture-placement heap.</summary>
    /// <param name="byteCount">The heap size in bytes.</param>
    /// <returns>The texture heap.</returns>
    public override GPUTextureHeap CreateTextureHeap(ulong byteCount)
    {
        VulkanTextureHeapOwner owner = new()
        {
            Device = this,
        };
        Vk.VkMemoryAllocateInfo allocateInfo = new()
        {
            allocationSize = byteCount,
            memoryTypeIndex = this.TextureMemoryType,
        };
        Backend.CheckVk(this.Api.vkAllocateMemory(&allocateInfo, null, out owner.Memory), "vkAllocateMemory (texture heap)", byteCount);
        return new GPUTextureHeap
        {
            Size = byteCount,
            Owner = owner,
        };
    }

    /// <summary>Places a texture in a heap.</summary>
    /// <param name="desc">The resource or operation description.</param>
    /// <param name="heap">The resource heap.</param>
    /// <param name="offset">The byte offset in the heap.</param>
    /// <returns>The texture.</returns>
    public override GPUTexture CreateTexture(TextureDesc desc, GPUTextureHeap heap, ulong offset)
    {
        VulkanTextureHeapOwner? owner = (VulkanTextureHeapOwner?)heap.Owner;
        Debug.Assert(owner != null && owner.Device == this);
        Backend.RequireTextureSamples(in desc);
        PreparedTexture texture = new();
        PrepareTexture(this, in desc, ref texture);

        VulkanTexture result = new()
        {
            Owner = this,
            Width = desc.Extent.X,
            Height = desc.Extent.Y,
            Depth = desc.Extent.Z,
            LayerCount = desc.LayerCount,
            SampleCount = desc.SampleCount,
            Type = desc.Type,
            Format = desc.Format,
            Usage = desc.Usage,
        };
        fixed (uint* QueueFamilies = this.QueueFamilies)
        fixed (Vk.VkFormat* viewFormatPointer = texture.ViewFormats)
        {
            texture.FormatList.pViewFormats = viewFormatPointer;
            texture.ImageInfo.pQueueFamilyIndices = QueueFamilies;
            texture.ImageInfo.pNext = texture.FormatList.viewFormatCount > 1 ? &texture.FormatList : null;
            if (desc.SampleCount > 1 && !SupportsImageCreateInfo(this, in texture.ImageInfo, out _))
                throw new GPUException(Error.Unsupported, "Vulkan cannot create the requested multisampled texture.");
            Backend.CheckVk(this.Api.vkCreateImage(&texture.ImageInfo, null, out Vk.VkImage image), "vkCreateImage");
            bool bound = false;
            try
            {
                Backend.CheckVk(this.Api.vkBindImageMemory(image, owner.Memory, offset), "vkBindImageMemory");
                result.Image = image;
                bound = true;
            }
            finally
            {
                if (!bound) this.Api.vkDestroyImage(image, null);
            }
        }
        return result;
    }

    /// <summary>Creates a command pool for the given queue.</summary>
    /// <param name="queueIndex">The queue index.</param>
    /// <returns>The command pool.</returns>
    public override GPUCommandPool CreateCommandPool(uint queueIndex)
    {
        Debug.Assert(queueIndex < this.QueueCount, "createCommandPool requires an available queue index");
        VulkanCommandPool pool = new() { Owner = this, QueueIndex = queueIndex, Timestamps = this.Queues![queueIndex].Timestamps };
        Vk.VkCommandPoolCreateInfo poolInfo = new()
        {
            flags = Vk.VkCommandPoolCreateFlags.Transient,
            queueFamilyIndex = this.Queues[queueIndex].FamilyIndex,
        };
        Backend.CheckVk(this.Api.vkCreateCommandPool(&poolInfo, null, out pool.Handle), "vkCreateCommandPool");
        return pool;
    }

}

internal sealed unsafe partial class VulkanTextureHeapOwner
{
    internal override void Destroy()
    {
        this.Device.Api.vkFreeMemory(this.Memory, null);
    }
}

internal sealed unsafe partial class VulkanTexture
{
    /// <summary>Releases the texture after every view and GPU use have finished.</summary>
    public override void Destroy()
    {
        Debug.Assert(this.Swapchain == null, "swapchain textures are borrowed");
        this.Owner.Api.vkDestroyImage(this.Image, null);
    }

    /// <summary>Creates a render-target view.</summary>
    /// <param name="desc">The resource or operation description.</param>
    /// <returns>The render view.</returns>
    public override GPURenderView CreateRenderView(RenderViewDesc desc)
    {
        Debug.Assert(this.Owner != null);

        uint width = this.Width >> (int)desc.MipLevel;
        uint height = this.Height >> (int)desc.MipLevel;
        if (width == 0) width = 1;
        if (height == 0) height = 1;
        VulkanRenderView result = new()
        {
            Owner = this.Owner,
            Source = this,
            Width = width,
            Height = height,
        };
        Vk.VkImageViewCreateInfo viewInfo = new()
        {
            image = this.Image,
            viewType = Vk.VkImageViewType.Image2D,
            format = Backend.ToVk(this.Format),
            subresourceRange = new Vk.VkImageSubresourceRange(Backend.ImageAspects(this.Format), desc.MipLevel, 1, desc.Slice, 1),
        };
        Backend.CheckVk(this.Owner.Api.vkCreateImageView(&viewInfo, null, out result.View), "vkCreateImageView");
        return result;
    }
}

internal sealed unsafe partial class VulkanRenderView
{
    /// <summary>The pixel format of the underlying attachment texture.</summary>
    public override Format Format => Source.Format;

    /// <summary>The sample count of the underlying attachment texture.</summary>
    public override uint SampleCount => Source.SampleCount == 0 ? 1u : Source.SampleCount;

    /// <summary>Releases the view after no recorded or executing render pass uses it.</summary>
    public override void Destroy()
    {
        Debug.Assert(!this.SwapchainView, "swapchain render views are owned by their swapchain");
        this.Owner.Api.vkDestroyImageView(this.View, null);
    }
}

internal sealed unsafe partial class VulkanTextureDescriptorHeap
{
    /// <summary>Releases the heap.</summary>
    public override void Destroy()
    {
        this.Storage.Destroy();
        this.Descriptors = null;
    }

    /// <summary>Binds a texture view into one heap slot.</summary>
    /// <param name="index">The destination descriptor index.</param>
    /// <param name="texture">The texture resource.</param>
    /// <param name="type">The descriptor or index type.</param>
    /// <param name="desc">The resource or operation description.</param>
    public override void WriteTextureDescriptor(uint index, GPUTexture texture, TextureDescriptorType type, TextureDescriptorDesc desc)
    {
        VulkanTexture vulkanTexture = (VulkanTexture)texture;
        Debug.Assert(vulkanTexture != null && index < this.Capacity && this.Owner == vulkanTexture.Owner && vulkanTexture.Swapchain == null);
        VulkanDeviceBase device = this.Owner;

        fixed (byte* descriptors = this.Descriptors)
        {
            Vk.VkImageViewUsageCreateInfo viewUsage;
            Vk.VkImageViewCreateInfo viewInfo = TextureDescriptorView(vulkanTexture, type, desc, &viewUsage);
            Vk.VkImageDescriptorInfoEXT imageDescriptor = new()
            {
                pView = &viewInfo,
                layout = Vk.VkImageLayout.General,
            };
            Vk.VkResourceDescriptorInfoEXT descriptorInfo = new()
            {
                type = type == TextureDescriptorType.Sampled
                    ? Vk.VkDescriptorType.SampledImage
                    : Vk.VkDescriptorType.StorageImage,
                data = new() { pImage = &imageDescriptor },
            };
            Vk.VkHostAddressRangeEXT destination = new()
            {
                address = descriptors + (ulong)index * device.HeapProperties.imageDescriptorSize,
                size = (nuint)device.HeapProperties.imageDescriptorSize,
            };
            Backend.AssertVk(device.Api.vkWriteResourceDescriptorsEXT(1, &descriptorInfo, &destination));
            Backend.NativeCopy(this.Storage.Range.Cpu + (ulong)index * device.HeapProperties.imageDescriptorSize, destination.address, destination.size);
        }
    }
}

internal sealed unsafe partial class VulkanSamplerDescriptorHeap
{
    /// <summary>Releases the heap.</summary>
    public override void Destroy()
    {
        this.Storage.Destroy();
        this.Descriptors = null;
    }

    /// <summary>Binds a sampler into one heap slot.</summary>
    /// <param name="index">The destination descriptor index.</param>
    /// <param name="desc">The resource or operation description.</param>
    public override void WriteSamplerDescriptor(uint index, SamplerDesc desc)
    {
        Debug.Assert(index < this.Capacity);
        VulkanDeviceBase device = this.Owner;
        fixed (byte* descriptors = this.Descriptors)
        {
            Vk.VkSamplerCreateInfo samplerInfo = SamplerInfo(desc);
            Vk.VkHostAddressRangeEXT destination = new()
            {
                address = descriptors + (ulong)index * device.HeapProperties.samplerDescriptorSize,
                size = (nuint)device.HeapProperties.samplerDescriptorSize,
            };
            Backend.AssertVk(device.Api.vkWriteSamplerDescriptorsEXT(1, &samplerInfo, &destination));
            Backend.NativeCopy(this.Storage.Range.Cpu + (ulong)index * device.HeapProperties.samplerDescriptorSize, destination.address, destination.size);
        }
    }
}

internal sealed unsafe partial class VulkanPso
{
    /// <summary>Releases the pipeline after no recorded or executing work uses it.</summary>
    public override void Destroy()
    {
        if (this.Owner != null && !this.Pipeline.IsNull) this.Owner.Api.vkDestroyPipeline(this.Pipeline, null);
    }
}

internal sealed unsafe partial class VulkanCommandPool
{
    /// <summary>Releases the pool and its command buffers.</summary>
    public override void Destroy()
    {
        while (this.First != null)
        {
            VulkanCommandBuffer commands = this.First;
            if (commands.Swapchain != null)
            {
                commands.Swapchain.AcquireCommands = null;
                commands.Swapchain = null;
            }
            this.First = commands.Next;
            if (!commands.TimestampPool.IsNull) this.Owner.Api.vkDestroyQueryPool(commands.TimestampPool, null);
            commands.TimestampDestinations = null;
            commands.TimestampResults = null;
        }
        this.Owner.Api.vkDestroyCommandPool(this.Handle, null);
    }
}
