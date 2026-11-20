// Raw C-layout roots and portable buffer, texture, and sampler access.

#pragma once

struct GPUBufferView
{
    uint Index;
    uint Offset;
};

// Wrapper types distinguish descriptor indices from ordinary root constants.
struct GpuTextureId
{
    uint index;
};

struct GpuSamplerId
{
    uint index;
};

#define GPU_ROOT_SELECT(_1, _2, _3, macro, ...) macro
#define GPU_ROOT(...) GPU_ROOT_SELECT(__VA_ARGS__, GPU_ROOT_IMPL, GPU_ROOT_DEFAULT)(__VA_ARGS__)
#define GPU_ROOT_DEFAULT(type, name) GPU_ROOT_IMPL(type, name, 16)
#define GPU_ROOT_IMPL(type, name, alignment) \
    typedef type GPURootArguments; \
    [[vk::binding(0, 0)]] ByteAddressBuffer name##_storage : register(t0); \
    static type name = name##_storage.Load<type>(0)

// Marks a generic root struct for composed-shader reflection (e.g. [GPURoot] struct PassRoot<T : ISurface>).
[__AttributeUsage(_AttributeTargets.Struct)]
public struct GPURootAttribute {};

#define GPU_ROOT_STORAGE(name) \
    [[vk::binding(0, 0)]] ByteAddressBuffer name##_storage : register(t0)

#define GPU_ROOT_LOAD(type, name) \
    type name = name##_storage.Load<type>(0)

[[vk::binding(3, 0)]] StructuredBuffer<uint64_t> gpu_buffer_table : register(t3);

[ForceInline]
T gpu_load<T>(GPUBufferView view, uint elementIndex = 0)
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
void gpu_store<T>(GPUBufferView view, uint elementIndex, T value)
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

// The view must address a four-byte-aligned uint; atomicity does not replace command barriers.
[ForceInline]
uint gpu_atomic_add_u32(GPUBufferView view, uint value)
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
GPUBufferView gpu_offset_bytes(GPUBufferView view, uint byteOffset)
{
    view.Offset += byteOffset;
    return view;
}

#ifdef NOGRAPHICSAPI_METAL

[[vk::binding(1)]] ConstantBuffer<uint64_t> gpu_texture_pool_base : register(b1);

// Metal requires a struct around resource elements in an argument buffer; each sampler still occupies exactly eight bytes.
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
T gpu_texture<T : IOpaqueDescriptor>(uint index)
{
    return gpu_texture_from_id<T>(gpu_texture_pool_base + uint64_t(index));
}

[ForceInline]
T gpu_sampler<T : IOpaqueDescriptor = SamplerState>(uint index)
{
    return gpu_sampler_value<T>(gpu_sampler_heap[index]);
}

#else

[ForceInline]
T gpu_texture<T : IOpaqueDescriptor>(uint index)
{
    return getDescriptorFromHandle(DescriptorHandle<T>(uint2(index, 0)));
}

[ForceInline]
T gpu_sampler<T : IOpaqueDescriptor = SamplerState>(uint index)
{
    return getDescriptorFromHandle(DescriptorHandle<T>(uint2(index, 0)));
}

#endif

[ForceInline]
uint gpu_nonuniform_index(uint index)
{
    __target_switch
    {
    case metal: return index;
    default: return NonUniformResourceIndex(index);
    }
}

[ForceInline]
uint gpu_wave_prefix_count_bits(bool value)
{
    __target_switch
    {
    case metal: __intrinsic_asm "simd_prefix_exclusive_sum(uint($0))";
    default: return WavePrefixCountBits(value);
    }
}
