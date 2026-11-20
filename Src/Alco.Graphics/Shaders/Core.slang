// Built-in Slang module for modular and composed Alco.Graphics shaders (`import Core;`).

module Core;

public struct GPUBufferView
{
    public uint Index;
    public uint Offset;
};

// Wrapper types distinguish descriptor indices from ordinary root constants.
public struct GpuTextureId
{
    public uint index;
};

public struct GpuSamplerId
{
    public uint index;
};

// Marks a generic root struct for composed-shader reflection (e.g. [GPURoot] struct PassRoot<T : ISurface>).
[__AttributeUsage(_AttributeTargets.Struct)]
public struct GPURootAttribute {};

[[vk::binding(0, 0)]] ByteAddressBuffer gpu_root_storage : register(t0);
[[vk::binding(3, 0)]] StructuredBuffer<uint64_t> gpu_buffer_table : register(t3);

[ForceInline]
public T gpu_load_root<T>()
{
    return gpu_root_storage.Load<T>(0);
}

[ForceInline]
public T gpu_load<T>(GPUBufferView view, uint elementIndex = 0)
{
    __target_switch
    {
    case metal:
    case spirv:
        return ((T*)(gpu_buffer_table[view.Index] + uint64_t(view.Offset)))[elementIndex];
    default:
        ByteAddressBuffer buffer = getDescriptorFromHandle(DescriptorHandle<ByteAddressBuffer>(uint2(view.Index, 0)));
        return buffer.Load<T>(view.Offset + uint(sizeof(T)) * elementIndex);
    }
}

[ForceInline]
public void gpu_store<T>(GPUBufferView view, uint elementIndex, T value)
{
    __target_switch
    {
    case metal:
    case spirv:
        ((T*)(gpu_buffer_table[view.Index] + uint64_t(view.Offset)))[elementIndex] = value;
        return;
    default:
        RWByteAddressBuffer buffer = getDescriptorFromHandle(DescriptorHandle<RWByteAddressBuffer>(uint2(view.Index, 0)));
        buffer.Store<T>(view.Offset + uint(sizeof(T)) * elementIndex, value);
        return;
    }
}

[ForceInline]
public uint gpu_atomic_add_u32(GPUBufferView view, uint value)
{
    uint previous;
    __target_switch
    {
    case metal:
    case spirv:
        InterlockedAdd(*((uint*)(gpu_buffer_table[view.Index] + uint64_t(view.Offset))), value, previous);
    default:
        RWByteAddressBuffer buffer = getDescriptorFromHandle(DescriptorHandle<RWByteAddressBuffer>(uint2(view.Index, 0)));
        buffer.InterlockedAdd(view.Offset, value, previous);
    }
    return previous;
}

[ForceInline]
public GPUBufferView gpu_offset_bytes(GPUBufferView view, uint byteOffset)
{
    view.Offset += byteOffset;
    return view;
}

#ifdef ALCO_GRAPHICS_METAL

[[vk::binding(1)]] ConstantBuffer<uint64_t> gpu_texture_pool_base : register(b1);

__target_intrinsic(metal, "GPUSampler")
struct GPUSampler { uint64_t value; };
[[vk::binding(2)]] StructuredBuffer<GPUSampler> gpu_sampler_heap : register(t2);

T gpu_sampler_value<T : IOpaqueDescriptor>(GPUSampler value)
{
    __requirePrelude(R"(struct GPUSampler { sampler value; }; static_assert(sizeof(GPUSampler) == 8);)");
    __intrinsic_asm "($0.value)";
}

T gpu_texture_from_id<T : IOpaqueDescriptor>(uint64_t resource_id)
{
    __requirePrelude(R"(template<typename T> T gpu_texture_from_id(ulong id) { return *reinterpret_cast<thread T*>(&id); })");
    __intrinsic_asm "gpu_texture_from_id<$TR>($0)";
}

[ForceInline]
public T gpu_texture<T : IOpaqueDescriptor>(uint index)
{
    return gpu_texture_from_id<T>(gpu_texture_pool_base + uint64_t(index));
}

[ForceInline]
public T gpu_sampler<T : IOpaqueDescriptor = SamplerState>(uint index)
{
    return gpu_sampler_value<T>(gpu_sampler_heap[index]);
}

#else

[ForceInline]
public T gpu_texture<T : IOpaqueDescriptor>(uint index)
{
    return getDescriptorFromHandle(DescriptorHandle<T>(uint2(index, 0)));
}

[ForceInline]
public T gpu_sampler<T : IOpaqueDescriptor = SamplerState>(uint index)
{
    return getDescriptorFromHandle(DescriptorHandle<T>(uint2(index, 0)));
}

#endif

[ForceInline]
public uint gpu_nonuniform_index(uint index)
{
    __target_switch
    {
    case metal: return index;
    default: return NonUniformResourceIndex(index);
    }
}

[ForceInline]
public uint gpu_wave_prefix_count_bits(bool value)
{
    __target_switch
    {
    case metal: __intrinsic_asm "simd_prefix_exclusive_sum(uint($0))";
    default: return WavePrefixCountBits(value);
    }
}
