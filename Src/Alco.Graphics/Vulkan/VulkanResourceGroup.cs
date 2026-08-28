using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// The bound resource set (wgpu BindGroup equivalent): a descriptor set allocated
/// from the layout and populated with the concrete resources.
/// </summary>
internal sealed unsafe class VulkanResourceGroup : GPUResourceGroup
{
    private readonly VulkanBindGroup _layout;
    private readonly IGPUBindableResource[] _resources;
    private VkDescriptorSet _set;

    protected override VulkanDevice Device { get; }

    public override IReadOnlyList<IGPUBindableResource> Resources
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resources;
    }

    public VkDescriptorSet NativeSet
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _set;
    }

    /// <summary>The texture views bound for sampling or storage (tracked for barriers).</summary>
    public IReadOnlyList<VulkanTextureView> BoundViews => _boundViews;
    /// <summary>The tracked state each bound view should be marked with (ShaderRead/ShaderWrite).</summary>
    public IReadOnlyList<VulkanResourceState> BoundViewStates => _boundViewStates;
    /// <summary>The buffers bound as uniform or storage (tracked for barriers).</summary>
    public IReadOnlyList<VulkanBuffer> BoundBuffers => _boundBuffers;
    /// <summary>The tracked state each bound buffer should be marked with (UniformRead/ShaderReadWrite).</summary>
    public IReadOnlyList<VulkanResourceState> BoundBufferStates => _boundBufferStates;

    private readonly List<VulkanTextureView> _boundViews = new();
    private readonly List<VulkanResourceState> _boundViewStates = new();
    private readonly List<VulkanBuffer> _boundBuffers = new();
    private readonly List<VulkanResourceState> _boundBufferStates = new();

    public VulkanResourceGroup(VulkanDevice device, in ResourceGroupDescriptor descriptor) : base(descriptor)
    {
        Device = device;
        _layout = (VulkanBindGroup)descriptor.Layout;

        int count = descriptor.Resources.Length;
        _resources = new IGPUBindableResource[count];
        for (int i = 0; i < count; i++)
        {
            _resources[i] = descriptor.Resources[i].Resource;
        }

        _set = _layout.AllocateSet();

        // build a lookup from binding index to descriptor metadata
        Dictionary<uint, BindGroupEntry> bindingInfos = new();
        foreach (BindGroupEntry entry in _layout.Bindings)
        {
            bindingInfos[entry.Binding] = entry;
        }

        int writeCount = count;
        VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[writeCount];
        VkDescriptorBufferInfo* bufferInfos = stackalloc VkDescriptorBufferInfo[writeCount];
        VkDescriptorImageInfo* imageInfos = stackalloc VkDescriptorImageInfo[writeCount];

        int writeIndex = 0;
        for (int i = 0; i < count; i++)
        {
            ResourceBindingEntry resourceEntry = descriptor.Resources[i];
            if (!bindingInfos.TryGetValue(resourceEntry.Binding, out BindGroupEntry layoutEntry))
            {
                throw new GraphicsException(
                    $"Resource group '{Name}' binds slot {resourceEntry.Binding} which is not part of the layout '{_layout.Name}'.");
            }

            VkDescriptorType descriptorType = VulkanUtility.BindingTypeToDescriptorType(layoutEntry.Type);
            uint binding = resourceEntry.Binding;

            switch (resourceEntry.Resource.ResourceType)
            {
                case BindableResourceType.Buffer:
                {
                    VulkanBuffer buffer = (VulkanBuffer)resourceEntry.Resource;
                    ulong range = resourceEntry.UseOffset ? resourceEntry.Size : buffer.Size;
                    ulong offset = resourceEntry.UseOffset ? resourceEntry.Offset : 0;
                    if (range == 0)
                    {
                        range = buffer.Size - offset;
                    }

                    bufferInfos[writeIndex] = new VkDescriptorBufferInfo
                    {
                        buffer = buffer.Native,
                        offset = offset,
                        range = range,
                    };

                    writes[writeIndex] = new VkWriteDescriptorSet
                    {
                        dstSet = _set,
                        dstBinding = binding,
                        dstArrayElement = 0,
                        descriptorCount = 1,
                        descriptorType = descriptorType,
                        pBufferInfo = &bufferInfos[writeIndex],
                    };
                    writeIndex++;
                    _boundBuffers.Add(buffer);
                    _boundBufferStates.Add(descriptorType == VkDescriptorType.UniformBuffer
                        ? VulkanResourceState.UniformRead
                        : VulkanResourceState.ShaderReadWrite);
                    break;
                }
                case BindableResourceType.Sampler:
                {
                    VulkanSampler sampler = (VulkanSampler)resourceEntry.Resource;
                    imageInfos[writeIndex] = new VkDescriptorImageInfo
                    {
                        sampler = sampler.Native,
                        imageView = default,
                        imageLayout = VkImageLayout.General,
                    };

                    writes[writeIndex] = new VkWriteDescriptorSet
                    {
                        dstSet = _set,
                        dstBinding = binding,
                        dstArrayElement = 0,
                        descriptorCount = 1,
                        descriptorType = descriptorType,
                        pImageInfo = &imageInfos[writeIndex],
                    };
                    writeIndex++;
                    break;
                }
                case BindableResourceType.TextureView:
                {
                    VulkanTextureView view = (VulkanTextureView)resourceEntry.Resource;

                    // storage texture access decides the tracked write state; sampled
                    // views are read-only bindings
                    imageInfos[writeIndex] = new VkDescriptorImageInfo
                    {
                        sampler = default,
                        imageView = view.Native,
                        imageLayout = VkImageLayout.General,
                    };

                    writes[writeIndex] = new VkWriteDescriptorSet
                    {
                        dstSet = _set,
                        dstBinding = binding,
                        dstArrayElement = 0,
                        descriptorCount = 1,
                        descriptorType = descriptorType,
                        pImageInfo = &imageInfos[writeIndex],
                    };
                    writeIndex++;
                    _boundViews.Add(view);
                    // storage textures with write access become ShaderWrite; sampled
                    // and read-only storage views are read-only bindings
                    bool storageWrite = layoutEntry.Type == BindingType.StorageTexture
                        && (layoutEntry.StorageTextureInfo.Access & AccessMode.Write) != 0;
                    _boundViewStates.Add(storageWrite
                        ? VulkanResourceState.ShaderWrite
                        : VulkanResourceState.ShaderRead);
                    break;
                }
                default:
                    throw new GraphicsException($"Unknown bindable resource type in resource group '{Name}'.");
            }
        }

        if (writeIndex > 0)
        {
            vkUpdateDescriptorSets(Device.NativeDevice, (uint)writeIndex, writes, 0, null);
        }

        Device.SetDebugName(VkObjectType.DescriptorSet, _set.Handle, descriptor.Name);
    }

    /// <summary>The storage access declared by the layout for a view binding, used to decide barriers.</summary>
    public static AccessMode GetStorageAccess(BindGroupEntry entry)
    {
        return entry.Type == BindingType.StorageTexture ? entry.StorageTextureInfo.Access : AccessMode.None;
    }

    public IReadOnlyList<BindGroupEntry> LayoutBindings => _layout.Bindings;

    protected override void Dispose(bool disposing)
    {
        if (_set.Handle == 0)
        {
            return;
        }

        // the set returns to the owning layout's pool free list; the pool itself is
        // destroyed with the layout
        _layout.FreeSet(_set);
        _set = default;
    }
}
