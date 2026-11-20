// Vulkan Compact descriptor-indexed heaps and their shared shader layout.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Vk = Vulkan;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe partial class VulkanCompactDevice
{
    internal const Vk.VkShaderStageFlags BindingStages = Vk.VkShaderStageFlags.Vertex | Vk.VkShaderStageFlags.Fragment | Vk.VkShaderStageFlags.Compute;
    internal const uint SampledBinding = 2;
    internal const uint StorageBinding = 3;
    private const uint PushConstantBytes = 2 * sizeof(ulong);

    private Vk.VkDescriptorSetLayout EmptySetLayout;
    private Vk.VkDescriptorSetLayout SampledSetLayout;
    private Vk.VkDescriptorSetLayout StorageSetLayout;
    private Vk.VkDescriptorSetLayout SamplerSetLayout;
    private Vk.VkDescriptorPool UndefinedTexturePool;
    internal Vk.VkDescriptorSet UndefinedSampledSet;
    internal Vk.VkDescriptorSet UndefinedStorageSet;
    private uint TextureLayoutCapacity;
    private uint SamplerLayoutCapacity;
    private uint ActiveSamplers;

    /// <summary>Creates the descriptor set and push constant layouts for the compact shader ABI.</summary>
    internal override void InitializeShaderBinding()
    {
        ref readonly Vk.VkPhysicalDeviceVulkan12Properties properties = ref Vulkan12Properties;
        uint poolBudget = properties.maxUpdateAfterBindDescriptorsInAllPools;
        uint stageResources = properties.maxPerStageUpdateAfterBindResources;
        uint colorAttachments = Math.Min(PhysicalProperties.limits.maxColorAttachments, Backend.MaxColorAttachments);
        if (PhysicalProperties.limits.maxBoundDescriptorSets < 4 || PhysicalProperties.limits.maxPushConstantsSize < PushConstantBytes ||
            poolBudget < 5 || stageResources < colorAttachments + 2)
        {
            throw VulkanBackend.CreationFailure(Error.Unsupported, "Vulkan Compact descriptor and push constant limits are insufficient.");
        }

        // Reserve an undefined descriptor per image type; standalone samplers count only against the pool budget here.
        poolBudget -= 2;
        uint samplerLimit = Math.Min(properties.maxPerStageDescriptorUpdateAfterBindSamplers, properties.maxDescriptorSetUpdateAfterBindSamplers);
        uint imageBudget = Math.Min(stageResources - colorAttachments, poolBudget - Math.Min(samplerLimit, poolBudget / 3)) / 2;
        uint sampledCapacity = Math.Min(imageBudget,
            Math.Min(properties.maxPerStageDescriptorUpdateAfterBindSampledImages, properties.maxDescriptorSetUpdateAfterBindSampledImages));
        uint storageCapacity = Math.Min(imageBudget,
            Math.Min(properties.maxPerStageDescriptorUpdateAfterBindStorageImages, properties.maxDescriptorSetUpdateAfterBindStorageImages));
        if (sampledCapacity == 0 || storageCapacity == 0 || samplerLimit == 0)
            throw VulkanBackend.CreationFailure(Error.Unsupported, "Vulkan Compact requires sampled image, storage image, and sampler descriptors.");

        // Set 0 is empty: the root and buffer-view table are physical addresses in push constants.
        Vk.VkDescriptorSetLayoutCreateInfo emptyInfo = new();
        Backend.CheckVk(Api.vkCreateDescriptorSetLayout(&emptyInfo, null, out Vk.VkDescriptorSetLayout emptyLayout), "vkCreateDescriptorSetLayout (empty)");
        EmptySetLayout = emptyLayout;
        SampledSetLayout = CreateBindingLayout(SampledBinding, Vk.VkDescriptorType.SampledImage, ref sampledCapacity);
        StorageSetLayout = CreateBindingLayout(StorageBinding, Vk.VkDescriptorType.StorageImage, ref storageCapacity);
        TextureLayoutCapacity = Math.Min(sampledCapacity, storageCapacity);
        SamplerLayoutCapacity = Math.Min(samplerLimit, poolBudget - 2 * TextureLayoutCapacity);
        SamplerSetLayout = CreateBindingLayout(0, Vk.VkDescriptorType.Sampler, ref SamplerLayoutCapacity);

        Vk.VkDescriptorSetLayout* layouts = stackalloc Vk.VkDescriptorSetLayout[4] { EmptySetLayout, SampledSetLayout, StorageSetLayout, SamplerSetLayout };
        Vk.VkPushConstantRange pushRange = new() { stageFlags = BindingStages, size = PushConstantBytes };
        Vk.VkPipelineLayoutCreateInfo layoutInfo = new()
        {
            setLayoutCount = 4,
            pSetLayouts = layouts,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &pushRange,
        };
        Backend.CheckVk(Api.vkCreatePipelineLayout(&layoutInfo, null, out Vk.VkPipelineLayout layout), "vkCreatePipelineLayout (Vulkan Compact)");
        ShaderLayout = layout;

        Vk.VkDescriptorSet* undefinedSets = stackalloc Vk.VkDescriptorSet[2];
        UndefinedTexturePool = AllocateTextureSets(1, undefinedSets);
        UndefinedSampledSet = undefinedSets[0];
        UndefinedStorageSet = undefinedSets[1];
        Capabilities.TextureDescriptorHeapCapacity = Math.Min(TextureLayoutCapacity, 8192u);
        Capabilities.SamplerDescriptorHeapCapacity = Math.Min(SamplerLayoutCapacity, 256u);
    }

    /// <summary>Releases the compact shader binding objects, including after partial initialization.</summary>
    internal override void DestroyShaderBinding()
    {
        if (!UndefinedTexturePool.IsNull) Api.vkDestroyDescriptorPool(UndefinedTexturePool, null);
        if (!ShaderLayout.IsNull) Api.vkDestroyPipelineLayout(ShaderLayout, null);
        if (!SamplerSetLayout.IsNull) Api.vkDestroyDescriptorSetLayout(SamplerSetLayout, null);
        if (!StorageSetLayout.IsNull) Api.vkDestroyDescriptorSetLayout(StorageSetLayout, null);
        if (!SampledSetLayout.IsNull) Api.vkDestroyDescriptorSetLayout(SampledSetLayout, null);
        if (!EmptySetLayout.IsNull) Api.vkDestroyDescriptorSetLayout(EmptySetLayout, null);
        UndefinedTexturePool = default;
        ShaderLayout = default;
        SamplerSetLayout = default;
        StorageSetLayout = default;
        SampledSetLayout = default;
        EmptySetLayout = default;
    }

    private Vk.VkDescriptorSetLayout CreateBindingLayout(uint bindingIndex, Vk.VkDescriptorType type, ref uint capacity)
    {
        Vk.VkDescriptorSetLayoutBinding binding = new() { binding = bindingIndex, descriptorType = type, descriptorCount = 1, stageFlags = BindingStages };
        Vk.VkDescriptorBindingFlags flags = Vk.VkDescriptorBindingFlags.UpdateAfterBind | Vk.VkDescriptorBindingFlags.UpdateUnusedWhilePending |
            Vk.VkDescriptorBindingFlags.PartiallyBound | Vk.VkDescriptorBindingFlags.VariableDescriptorCount;
        Vk.VkDescriptorSetLayoutBindingFlagsCreateInfo bindingFlags = new() { bindingCount = 1, pBindingFlags = &flags };
        Vk.VkDescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            pNext = &bindingFlags,
            flags = Vk.VkDescriptorSetLayoutCreateFlags.UpdateAfterBindPool,
            bindingCount = 1,
            pBindings = &binding,
        };
        Vk.VkDescriptorSetVariableDescriptorCountLayoutSupport variableSupport = new();
        Vk.VkDescriptorSetLayoutSupport support = new() { pNext = &variableSupport };
        Api.vkGetDescriptorSetLayoutSupport(&layoutInfo, &support);
        if (!support.supported || variableSupport.maxVariableDescriptorCount == 0)
            throw VulkanBackend.CreationFailure(Error.Unsupported, $"Vulkan Compact cannot create a variable-count {type} descriptor set layout.");
        capacity = Math.Min(capacity, variableSupport.maxVariableDescriptorCount);
        binding.descriptorCount = capacity;
        Backend.CheckVk(Api.vkCreateDescriptorSetLayout(&layoutInfo, null, out Vk.VkDescriptorSetLayout layout), "vkCreateDescriptorSetLayout (heap)");
        return layout;
    }

    /// <summary>Creates a texture descriptor heap with sampled and storage image slots.</summary>
    /// <param name="capacity">The number of descriptor slots.</param>
    /// <returns>The texture descriptor heap.</returns>
    public override GPUTextureDescriptorHeap CreateTextureDescriptorHeap(uint capacity)
    {
        if (capacity == 0 || capacity > TextureLayoutCapacity)
            throw VulkanBackend.CreationFailure(Error.Unsupported, $"Vulkan Compact texture heap capacity must be in [1, {TextureLayoutCapacity}].");
        Vk.VkDescriptorSet* sets = stackalloc Vk.VkDescriptorSet[2];
        Vk.VkDescriptorPool pool = AllocateTextureSets(capacity, sets);
        return new VulkanCompactTextureDescriptorHeap(this, capacity, pool, sets[0], sets[1]);
    }

    /// <summary>Creates a sampler descriptor heap.</summary>
    /// <param name="capacity">The number of descriptor slots.</param>
    /// <returns>The sampler descriptor heap.</returns>
    public override GPUSamplerDescriptorHeap CreateSamplerDescriptorHeap(uint capacity)
    {
        if (capacity == 0 || capacity > SamplerLayoutCapacity)
            throw VulkanBackend.CreationFailure(Error.Unsupported, $"Vulkan Compact sampler heap capacity must be in [1, {SamplerLayoutCapacity}].");
        Vk.VkDescriptorSetLayout layout = SamplerSetLayout;
        Vk.VkDescriptorPoolSize size = new() { type = Vk.VkDescriptorType.Sampler, descriptorCount = capacity };
        Vk.VkDescriptorSet set;
        Vk.VkDescriptorPool pool = AllocateHeapSets(capacity, 1, &layout, &size, &set);
        return new VulkanCompactSamplerDescriptorHeap(this, capacity, pool, set);
    }

    private Vk.VkDescriptorPool AllocateTextureSets(uint capacity, Vk.VkDescriptorSet* sets)
    {
        Vk.VkDescriptorSetLayout* layouts = stackalloc Vk.VkDescriptorSetLayout[2] { SampledSetLayout, StorageSetLayout };
        Vk.VkDescriptorPoolSize* sizes = stackalloc Vk.VkDescriptorPoolSize[2]
        {
            new() { type = Vk.VkDescriptorType.SampledImage, descriptorCount = capacity },
            new() { type = Vk.VkDescriptorType.StorageImage, descriptorCount = capacity },
        };
        return AllocateHeapSets(capacity, 2, layouts, sizes, sets);
    }

    private Vk.VkDescriptorPool AllocateHeapSets(uint capacity, uint setCount, Vk.VkDescriptorSetLayout* layouts,
                                                Vk.VkDescriptorPoolSize* sizes, Vk.VkDescriptorSet* sets)
    {
        Vk.VkDescriptorPoolCreateInfo poolInfo = new()
        {
            flags = Vk.VkDescriptorPoolCreateFlags.UpdateAfterBind,
            maxSets = setCount,
            poolSizeCount = setCount,
            pPoolSizes = sizes,
        };
        Backend.CheckVk(Api.vkCreateDescriptorPool(&poolInfo, null, out Vk.VkDescriptorPool pool), "vkCreateDescriptorPool (heap)");
        uint* counts = stackalloc uint[2] { capacity, capacity };
        Vk.VkDescriptorSetVariableDescriptorCountAllocateInfo variableCounts = new() { descriptorSetCount = setCount, pDescriptorCounts = counts };
        Vk.VkDescriptorSetAllocateInfo allocateInfo = new()
        {
            pNext = &variableCounts,
            descriptorPool = pool,
            descriptorSetCount = setCount,
            pSetLayouts = layouts,
        };
        Vk.VkResult result = Api.vkAllocateDescriptorSets(&allocateInfo, sets);
        if (result != Vk.VkResult.Success)
        {
            Api.vkDestroyDescriptorPool(pool, null);
            Backend.CheckVk(result, "vkAllocateDescriptorSets (heap)");
        }
        return pool;
    }

    internal Vk.VkSampler CreateDescriptorSampler(SamplerDesc desc)
    {
        uint count;
        do
        {
            count = Volatile.Read(ref ActiveSamplers);
            if (count >= PhysicalProperties.limits.maxSamplerAllocationCount)
                throw VulkanBackend.CreationFailure(Error.Unsupported, "Vulkan Compact has reached its limit of live native samplers.");
        } while (Interlocked.CompareExchange(ref ActiveSamplers, count + 1, count) != count);

        Vk.VkSamplerCreateInfo samplerInfo = VulkanBackend.SamplerInfo(desc);
        Vk.VkResult result = Api.vkCreateSampler(&samplerInfo, null, out Vk.VkSampler sampler);
        if (result != Vk.VkResult.Success)
        {
            Interlocked.Decrement(ref ActiveSamplers);
            Backend.CheckVk(result, "vkCreateSampler (heap descriptor)");
        }
        return sampler;
    }

    internal void DestroyDescriptorSampler(Vk.VkSampler sampler)
    {
        Api.vkDestroySampler(sampler, null);
        Interlocked.Decrement(ref ActiveSamplers);
    }
}

internal sealed unsafe class VulkanCompactTextureDescriptorHeap : GPUTextureDescriptorHeap
{
    internal readonly VulkanCompactDevice Owner;
    internal readonly Vk.VkDescriptorSet SampledSet;
    internal readonly Vk.VkDescriptorSet StorageSet;
    private readonly Vk.VkDescriptorPool Pool;
    private readonly VulkanCompactDescriptor** Slots;

    internal VulkanCompactTextureDescriptorHeap(VulkanCompactDevice owner, uint capacity, Vk.VkDescriptorPool pool,
                                                Vk.VkDescriptorSet sampledSet, Vk.VkDescriptorSet storageSet)
    {
        Owner = owner;
        Capacity = capacity;
        Pool = pool;
        SampledSet = sampledSet;
        StorageSet = storageSet;
        Slots = (VulkanCompactDescriptor**)NativeMemory.AllocZeroed(capacity, (nuint)sizeof(VulkanCompactDescriptor*));
    }

    /// <summary>Releases the heap and its references to texture views.</summary>
    public override void Destroy()
    {
        Owner.Api.vkDestroyDescriptorPool(Pool, null);
        VulkanCompactDescriptor.ReleaseSlots(Owner, Slots, Capacity);
    }

    /// <summary>Binds a sampled or storage texture view into one heap slot.</summary>
    /// <param name="index">The destination slot.</param>
    /// <param name="texture">The referenced texture.</param>
    /// <param name="type">The descriptor access type.</param>
    /// <param name="desc">The texture view description.</param>
    public override void WriteTextureDescriptor(uint index, GPUTexture texture, TextureDescriptorType type, TextureDescriptorDesc desc)
    {
        VulkanTexture vulkanTexture = (VulkanTexture)texture;
        Debug.Assert(index < Capacity && vulkanTexture.Owner == Owner && vulkanTexture.Swapchain == null);
        Debug.Assert(type is TextureDescriptorType.Sampled or TextureDescriptorType.Storage);
        Vk.VkImageViewUsageCreateInfo usage;
        Vk.VkImageViewCreateInfo viewInfo = VulkanBackend.TextureDescriptorView(vulkanTexture, type, desc, &usage);
        Backend.CheckVk(Owner.Api.vkCreateImageView(&viewInfo, null, out Vk.VkImageView view), "vkCreateImageView (heap descriptor)");

        bool storage = type == TextureDescriptorType.Storage;
        Vk.VkDescriptorType nativeType = storage ? Vk.VkDescriptorType.StorageImage : Vk.VkDescriptorType.SampledImage;
        VulkanCompactDescriptor* current = VulkanCompactDescriptor.Create(view.Handle, nativeType);
        VulkanCompactDescriptor* previous = Slots[index];
        Vk.VkDescriptorImageInfo image = new() { imageView = view, imageLayout = Vk.VkImageLayout.General };
        Vk.VkWriteDescriptorSet write = new()
        {
            dstSet = storage ? StorageSet : SampledSet,
            dstBinding = storage ? VulkanCompactDevice.StorageBinding : VulkanCompactDevice.SampledBinding,
            dstArrayElement = index,
            descriptorCount = 1,
            descriptorType = nativeType,
            pImageInfo = &image,
        };
        if (previous != null && previous->Type != nativeType)
        {
            // Copying an undefined descriptor clears the old binding without requiring null descriptors.
            uint oldBinding = storage ? VulkanCompactDevice.SampledBinding : VulkanCompactDevice.StorageBinding;
            Vk.VkCopyDescriptorSet clear = new()
            {
                srcSet = storage ? Owner.UndefinedSampledSet : Owner.UndefinedStorageSet,
                srcBinding = oldBinding,
                dstSet = storage ? SampledSet : StorageSet,
                dstBinding = oldBinding,
                dstArrayElement = index,
                descriptorCount = 1,
            };
            Owner.Api.vkUpdateDescriptorSets(1, &write, 1, &clear);
        }
        else
        {
            Owner.Api.vkUpdateDescriptorSets(1, &write, 0, null);
        }
        Slots[index] = current;
        VulkanCompactDescriptor.Release(Owner, previous);
    }

    /// <summary>Copies texture descriptors between heaps, allowing overlapping ranges.</summary>
    /// <param name="sourceIndex">The first source slot.</param>
    /// <param name="destination">The destination heap.</param>
    /// <param name="destinationIndex">The first destination slot.</param>
    /// <param name="count">The number of slots to copy.</param>
    public override void CopyTextureDescriptors(uint sourceIndex, GPUTextureDescriptorHeap destination, uint destinationIndex, uint count)
    {
        VulkanCompactTextureDescriptorHeap target = (VulkanCompactTextureDescriptorHeap)destination;
        Debug.Assert(Owner == target.Owner && sourceIndex <= Capacity && count <= Capacity - sourceIndex);
        Debug.Assert(destinationIndex <= target.Capacity && count <= target.Capacity - destinationIndex);
        Vk.VkCopyDescriptorSet* copies = stackalloc Vk.VkCopyDescriptorSet[2]
        {
            new() { srcSet = SampledSet, srcBinding = VulkanCompactDevice.SampledBinding, dstSet = target.SampledSet,
                    dstBinding = VulkanCompactDevice.SampledBinding },
            new() { srcSet = StorageSet, srcBinding = VulkanCompactDevice.StorageBinding, dstSet = target.StorageSet,
                    dstBinding = VulkanCompactDevice.StorageBinding },
        };
        VulkanCompactDescriptor.Copy(Owner, Slots, target.Slots, sourceIndex, destinationIndex, count, copies, 2);
    }
}

internal sealed unsafe class VulkanCompactSamplerDescriptorHeap : GPUSamplerDescriptorHeap
{
    internal readonly VulkanCompactDevice Owner;
    internal readonly Vk.VkDescriptorSet Set;
    private readonly Vk.VkDescriptorPool Pool;
    private readonly VulkanCompactDescriptor** Slots;

    internal VulkanCompactSamplerDescriptorHeap(VulkanCompactDevice owner, uint capacity, Vk.VkDescriptorPool pool, Vk.VkDescriptorSet set)
    {
        Owner = owner;
        Capacity = capacity;
        Pool = pool;
        Set = set;
        Slots = (VulkanCompactDescriptor**)NativeMemory.AllocZeroed(capacity, (nuint)sizeof(VulkanCompactDescriptor*));
    }

    /// <summary>Releases the heap and its references to samplers.</summary>
    public override void Destroy()
    {
        Owner.Api.vkDestroyDescriptorPool(Pool, null);
        VulkanCompactDescriptor.ReleaseSlots(Owner, Slots, Capacity);
    }

    /// <summary>Binds a sampler into one heap slot.</summary>
    /// <param name="index">The destination slot.</param>
    /// <param name="desc">The sampler description.</param>
    public override void WriteSamplerDescriptor(uint index, SamplerDesc desc)
    {
        Debug.Assert(index < Capacity);
        Vk.VkSampler sampler = Owner.CreateDescriptorSampler(desc);
        VulkanCompactDescriptor* current = VulkanCompactDescriptor.Create(sampler.Handle, Vk.VkDescriptorType.Sampler);
        Vk.VkDescriptorImageInfo image = new() { sampler = sampler };
        Vk.VkWriteDescriptorSet write = new()
        {
            dstSet = Set,
            dstArrayElement = index,
            descriptorCount = 1,
            descriptorType = Vk.VkDescriptorType.Sampler,
            pImageInfo = &image,
        };
        Owner.Api.vkUpdateDescriptorSets(1, &write, 0, null);
        VulkanCompactDescriptor* previous = Slots[index];
        Slots[index] = current;
        VulkanCompactDescriptor.Release(Owner, previous);
    }

    /// <summary>Copies sampler descriptors between heaps, allowing overlapping ranges.</summary>
    /// <param name="sourceIndex">The first source slot.</param>
    /// <param name="destination">The destination heap.</param>
    /// <param name="destinationIndex">The first destination slot.</param>
    /// <param name="count">The number of slots to copy.</param>
    public override void CopySamplerDescriptors(uint sourceIndex, GPUSamplerDescriptorHeap destination, uint destinationIndex, uint count)
    {
        VulkanCompactSamplerDescriptorHeap target = (VulkanCompactSamplerDescriptorHeap)destination;
        Debug.Assert(Owner == target.Owner && sourceIndex <= Capacity && count <= Capacity - sourceIndex);
        Debug.Assert(destinationIndex <= target.Capacity && count <= target.Capacity - destinationIndex);
        Vk.VkCopyDescriptorSet copy = new() { srcSet = Set, dstSet = target.Set };
        VulkanCompactDescriptor.Copy(Owner, Slots, target.Slots, sourceIndex, destinationIndex, count, &copy, 1);
    }
}

internal unsafe struct VulkanCompactDescriptor
{
    internal ulong Handle;
    internal Vk.VkDescriptorType Type;
    internal int ReferenceCount;

    internal static VulkanCompactDescriptor* Create(ulong handle, Vk.VkDescriptorType type)
    {
        VulkanCompactDescriptor* descriptor = (VulkanCompactDescriptor*)NativeMemory.Alloc((nuint)sizeof(VulkanCompactDescriptor));
        *descriptor = new VulkanCompactDescriptor { Handle = handle, Type = type, ReferenceCount = 1 };
        return descriptor;
    }

    internal static void Release(VulkanCompactDevice device, VulkanCompactDescriptor* descriptor)
    {
        if (descriptor == null || Interlocked.Decrement(ref descriptor->ReferenceCount) != 0) return;
        if (descriptor->Type == Vk.VkDescriptorType.Sampler)
            device.DestroyDescriptorSampler(new Vk.VkSampler(descriptor->Handle));
        else
            device.Api.vkDestroyImageView(new Vk.VkImageView(descriptor->Handle), null);
        NativeMemory.Free(descriptor);
    }

    internal static void ReleaseSlots(VulkanCompactDevice device, VulkanCompactDescriptor** slots, uint capacity)
    {
        for (uint index = 0; index < capacity; ++index)
            Release(device, slots[index]);
        NativeMemory.Free(slots);
    }

    internal static void Copy(VulkanCompactDevice device, VulkanCompactDescriptor** sourceSlots, VulkanCompactDescriptor** destinationSlots,
                              uint sourceIndex, uint destinationIndex, uint count, Vk.VkCopyDescriptorSet* copies, uint copyCount)
    {
        bool sameHeap = sourceSlots == destinationSlots;
        if (count == 0 || (sameHeap && sourceIndex == destinationIndex)) return;
        bool backwards = sameHeap && destinationIndex > sourceIndex;
        // Each native copy must be disjoint even when the public operation overlaps.
        uint chunkLimit = sameHeap ? (backwards ? destinationIndex - sourceIndex : sourceIndex - destinationIndex) : count;
        while (count != 0)
        {
            uint length = Math.Min(count, chunkLimit);
            uint offset = backwards ? count - length : 0;
            for (uint index = 0; index < copyCount; ++index)
            {
                copies[index].srcArrayElement = sourceIndex + offset;
                copies[index].dstArrayElement = destinationIndex + offset;
                copies[index].descriptorCount = length;
            }
            device.Api.vkUpdateDescriptorSets(0, null, copyCount, copies);
            for (uint index = offset; index < offset + length; ++index)
            {
                VulkanCompactDescriptor* current = sourceSlots[sourceIndex + index];
                VulkanCompactDescriptor** destination = &destinationSlots[destinationIndex + index];
                VulkanCompactDescriptor* previous = *destination;
                if (current == previous) continue;
                if (current != null) Interlocked.Increment(ref current->ReferenceCount);
                *destination = current;
                Release(device, previous);
            }
            count -= length;
            if (!backwards)
            {
                sourceIndex += length;
                destinationIndex += length;
            }
        }
    }
}
