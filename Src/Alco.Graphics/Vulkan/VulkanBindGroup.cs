using System.Runtime.CompilerServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Alco.Graphics.Vulkan;

/// <summary>
/// The bind group LAYOUT (wgpu BindGroupLayout equivalent): builds a
/// VkDescriptorSetLayout from the abstract binding entries and manages the
/// descriptor pools that sets created from this layout are allocated from.
/// </summary>
internal sealed unsafe class VulkanBindGroup : GPUBindGroup
{
    private const int SetsPerPool = 64;

    private VkDescriptorSetLayout _layout;
    private readonly BindGroupEntry[] _bindings;

    // pool batching: sets are allocated from lazily created pools and recycled
    // through a free list when the owning resource group is destroyed.
    private readonly List<VkDescriptorPool> _pools = new();
    private readonly List<VkDescriptorSet> _freeSets = new();
    private int _allocatedFromCurrentPool;
    private readonly VulkanDevice _device;

    // cached pool size request for this layout
    private VkDescriptorPoolSize[] _poolSizes;

    public VkDescriptorSetLayout NativeLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _layout;
    }

    public override IReadOnlyList<BindGroupEntry> Bindings
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _bindings;
    }

    protected override GPUDevice Device => _device;

    public VulkanBindGroup(VulkanDevice device, in BindGroupDescriptor descriptor) : base(descriptor)
    {
        _device = device;
        _bindings = new BindGroupEntry[descriptor.Bindings.Length];
        Array.Copy(descriptor.Bindings, _bindings, descriptor.Bindings.Length);

        int count = _bindings.Length;
        VkDescriptorSetLayoutBinding* nativeBindings = stackalloc VkDescriptorSetLayoutBinding[count];

        Dictionary<VkDescriptorType, uint> typeCounts = new();

        for (int i = 0; i < count; i++)
        {
            BindGroupEntry entry = _bindings[i];
            VkDescriptorType descriptorType = VulkanUtility.BindingTypeToDescriptorType(entry.Type);
            nativeBindings[i] = new VkDescriptorSetLayoutBinding
            {
                binding = entry.Binding,
                descriptorType = descriptorType,
                descriptorCount = 1,
                stageFlags = VulkanUtility.ConvertShaderStage(entry.Stage),
                pImmutableSamplers = null,
            };

            typeCounts.TryGetValue(descriptorType, out uint current);
            typeCounts[descriptorType] = current + 1;
        }

        _poolSizes = new VkDescriptorPoolSize[typeCounts.Count];
        int poolSizeIndex = 0;
        foreach (KeyValuePair<VkDescriptorType, uint> pair in typeCounts)
        {
            _poolSizes[poolSizeIndex++] = new VkDescriptorPoolSize
            {
                type = pair.Key,
                descriptorCount = pair.Value * SetsPerPool,
            };
        }

        VkDescriptorSetLayoutCreateInfo layoutInfo = new()
        {
            bindingCount = (uint)count,
            pBindings = nativeBindings,
        };

        VkDescriptorSetLayout nativeLayout = default;
        VkResult result = vkCreateDescriptorSetLayout(_device.NativeDevice, &layoutInfo, null, &nativeLayout);
        _layout = nativeLayout;
        VulkanException.ThrowIfFailed(result, $"Failed to create bind group layout '{descriptor.Name}'");

        _device.SetDebugName(VkObjectType.DescriptorSetLayout, _layout.Handle, descriptor.Name);
    }

    /// <summary>Allocates (or recycles) a descriptor set compatible with this layout.</summary>
    public VkDescriptorSet AllocateSet()
    {
        if (_freeSets.Count > 0)
        {
            VkDescriptorSet recycled = _freeSets[^1];
            _freeSets.RemoveAt(_freeSets.Count - 1);
            return recycled;
        }

        if (_pools.Count == 0 || _allocatedFromCurrentPool >= SetsPerPool)
        {
            CreatePool();
        }

        VkDescriptorSetLayout layout = _layout;
        VkDescriptorSetAllocateInfo allocateInfo = new()
        {
            descriptorPool = _pools[^1],
            descriptorSetCount = 1,
            pSetLayouts = &layout,
        };

        VkDescriptorSet set = default;
        VkResult result = vkAllocateDescriptorSets(_device.NativeDevice, &allocateInfo, &set);
        VulkanException.ThrowIfFailed(result, "Failed to allocate descriptor set");
        _allocatedFromCurrentPool++;
        return set;
    }

    /// <summary>Returns a descriptor set to the layout's free list.</summary>
    public void FreeSet(VkDescriptorSet set)
    {
        if (set.Handle != 0)
        {
            _freeSets.Add(set);
        }
    }

    private void CreatePool()
    {
        VkDescriptorPoolCreateInfo poolInfo = new()
        {
            flags = VkDescriptorPoolCreateFlags.FreeDescriptorSet,
            maxSets = SetsPerPool,
            poolSizeCount = (uint)_poolSizes.Length,
        };

        fixed (VkDescriptorPoolSize* sizes = _poolSizes)
        {
            poolInfo.pPoolSizes = sizes;
            VkDescriptorPool pool = default;
            VkResult result = vkCreateDescriptorPool(_device.NativeDevice, &poolInfo, null, &pool);
            VulkanException.ThrowIfFailed(result, $"Failed to create descriptor pool for bind group '{Name}'");
            _pools.Add(pool);
            _device.SetDebugName(VkObjectType.DescriptorPool, pool.Handle, Name + "_pool");
        }

        _allocatedFromCurrentPool = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (_layout.Handle == 0)
        {
            return;
        }

        VkDevice nativeDevice = _device.NativeDevice;
        vkDestroyDescriptorSetLayout(nativeDevice, _layout, null);

        foreach (VkDescriptorPool pool in _pools)
        {
            vkDestroyDescriptorPool(nativeDevice, pool, null);
        }
        _pools.Clear();
        _freeSets.Clear();
        _layout = default;
    }
}
