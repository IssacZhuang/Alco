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

    // resource groups are created from worker threads too (thread-safe creation
    // contract), so pool state mutated during allocation needs a gate
    private readonly object _gate = new();

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
        VulkanException.ThrowIfFailed(result, $"Failed to create bind group layout '{descriptor.Name}'");
        _layout = nativeLayout;

        _device.SetDebugName(VkObjectType.DescriptorSetLayout, _layout.Handle, descriptor.Name);
    }

    /// <summary>Allocates (or recycles) a descriptor set compatible with this layout.</summary>
    public VkDescriptorSet AllocateSet()
    {
        lock (_gate)
        {
            if (_layout.Handle == 0)
            {
                throw new GraphicsException($"Bind group layout '{Name}' is disposed.");
            }

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
    }

    /// <summary>Retires a descriptor set returned by a destroyed resource group.
    /// The set may still be referenced by in-flight command buffers, so it only
    /// becomes recyclable after the device's frame-delayed retirement.</summary>
    public void RetireSet(VkDescriptorSet set)
    {
        if (set.Handle != 0)
        {
            _device.QueueDescriptorSetRetirement(this, set);
        }
    }

    /// <summary>Returns a retired set to the free list; called by the device once
    /// the frame delay guarantees no command buffer still references it.</summary>
    internal void RecycleSet(VkDescriptorSet set)
    {
        lock (_gate)
        {
            if (_layout.Handle != 0 && set.Handle != 0)
            {
                _freeSets.Add(set);
            }
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
        lock (_gate)
        {
            if (_layout.Handle == 0)
            {
                return;
            }

            VkDescriptorSetLayout layout = _layout;
            _layout = default;

            // pools and the layout are queued for deferred destruction: sets
            // still referenced by in-flight command buffers must stay valid,
            // and destroying a pool implicitly frees every set it owns
            foreach (VkDescriptorPool pool in _pools)
            {
                _device.QueueNativeDestroy(pool);
            }
            _pools.Clear();
            _freeSets.Clear();
            _device.QueueNativeDestroy(layout);
        }
    }
}
