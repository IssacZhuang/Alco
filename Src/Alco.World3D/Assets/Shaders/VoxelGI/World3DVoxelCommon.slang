#language slang 2025
module World3DVoxelCommon;

import Core;
import World3DShadowSample;

public static const uint WORLD3D_VOXEL_CASCADE_COUNT = 4u;
public static const uint WORLD3D_VOXEL_BRICK_SIZE = 8u;
public static const uint WORLD3D_VOXEL_BRICK_VOXELS = 512u;
public static const uint WORLD3D_VOXEL_BRICK_UINT4_STRIDE = 512u;
public static const uint WORLD3D_VOXEL_BRICK_UNCHECKED = 0xFFFFFFFFu;
public static const uint WORLD3D_VOXEL_BRICK_EMPTY = 0xFFFFFFFEu;
public static const uint WORLD3D_VOXEL_INVALID_INDEX = 0xFFFFFFFFu;

public struct World3DVoxelCascadeGpu
{
    public float4 OriginVoxelSize; // xyz = anchor-relative origin corner, w = voxel size (meters)
    public uint ToroidalOriginX;
    public uint ToroidalOriginY;
    public uint ToroidalOriginZ;
    public uint BricksPerAxis;
};

public struct World3DVoxelEvictBoxGpu
{
    public int MinLocalX;
    public int MinLocalY;
    public int MinLocalZ;
    public int CascadeIndex;
    public int MaxLocalExclX;
    public int MaxLocalExclY;
    public int MaxLocalExclZ;
    public int ResetFlag;
};

public struct World3DVoxelCandidateGpu
{
    public uint InstanceSlot;
    public uint CascadeIndex;
    public uint Lod;
    public uint TriangleCount;
};

public struct World3DVoxelChunkGpu
{
    public uint CandidateIndex;
    public uint StartTriangle;
};

// Matches World3DVoxelGiGpuCounters (40 bytes / 10 uints).
public struct World3DVoxelCountersGpu
{
    public uint StaticUsedBricks;
    public uint StaticAllocatedThisFrame;
    public uint StaticReclaimedThisFrame;
    public uint StaticFallbackBricks;
    public uint StaticDirtyQueued;
    public uint StaticDirtyDispatched;
    public uint DynamicUsedBricks;
    public uint DynamicFallbackBricks;
    public uint ChunkCandidateInstances;
    public uint BrickDispatchChunks;
};

public struct World3DVoxelFrameGpu
{
    public World3DCascadeGpu RsmCascade;
    public row_major float4x4 InvRsmLightViewProj;
    public float4 SunColor;
    public float4 SunDirIntensity;
    public float4 SkyHorizonAndAmbientFloor;
    public float4 SkyZenithAndFrameIndex;
    public uint Budget0;
    public uint Budget1;
    public uint Budget2;
    public uint Budget3;
    public GPUBufferView Cascades;
    public GPUBufferView EvictBoxes;
    public GPUBufferView PoolState;
    public GPUBufferView StaticFreeList;
    public GPUBufferView StaticPageTable;
    public GPUBufferView StaticReverseTable;
    public GPUBufferView StaticDirtyMask;
    public GPUBufferView DirtyQueue;
    public GPUBufferView ActiveDirtyList;
    public GPUBufferView DynamicDesiredMask;
    public GPUBufferView DynamicPageTable;
    public GPUBufferView DynamicReverseTable;
    public GPUBufferView DynamicActiveList;
    public GPUBufferView StaticBrickPool;
    public GPUBufferView DynamicBrickPool;
    public GPUBufferView Counters;
    public GPUBufferView PointLights;
    public GPUBufferView ReservedView0;
    public GPUTextureId BlueNoiseTex;
    public GPUTextureId RsmDepthTex;
    public GPUTextureId Vol0;
    public GPUTextureId Vol1;
    public GPUTextureId Vol2;
    public GPUTextureId Vol3;
    public uint BricksPerAxis;
    public uint StaticCapacity;
    public uint DynamicCapacity;
    public uint DynamicLevelCount;
    public uint EvictBoxCount;
    public uint StaticMaskWords;
    public uint DynamicMaskWords;
    public uint TotalStaticSlots;
    public uint TotalDynamicSlots;
    public uint PointLightCount;
    public float RsmDepthRange;
    public float EmissiveBoost;
    public float IndirectDiffuseIntensity;
    public float IndirectSpecularIntensity;
    public uint HasRsm;
    public uint HalfResMode;
    public uint TraceWidth;
    public uint TraceHeight;
    public uint FullWidth;
    public uint FullHeight;
    public uint Reserved0;
    public uint Reserved1;
};

// PoolState byte/uint layout (12 uints = 48 bytes):
// [0] StaticFreeTop
// [1] StaticFreeTopSnapshot
// [2] DynamicAllocCursor
// [3] Reserved0
// [4..7] CascadeAllocated[0..3]
// [8..11] CascadeBudget[0..3]
public static const uint VOXEL_STATE_FREE_TOP = 0u;
public static const uint VOXEL_STATE_FREE_TOP_SNAP = 1u;
public static const uint VOXEL_STATE_DYNAMIC_CURSOR = 2u;
public static const uint VOXEL_STATE_CASCADE_ALLOC_BASE = 4u;
public static const uint VOXEL_STATE_CASCADE_BUDGET_BASE = 8u;

public static const uint VOXEL_COUNTER_STATIC_USED = 0u;
public static const uint VOXEL_COUNTER_STATIC_ALLOCATED = 1u;
public static const uint VOXEL_COUNTER_STATIC_RECLAIMED = 2u;
public static const uint VOXEL_COUNTER_STATIC_FALLBACK = 3u;
public static const uint VOXEL_COUNTER_STATIC_DIRTY_QUEUED = 4u;
public static const uint VOXEL_COUNTER_STATIC_DIRTY_DISPATCHED = 5u;
public static const uint VOXEL_COUNTER_DYNAMIC_USED = 6u;
public static const uint VOXEL_COUNTER_DYNAMIC_FALLBACK = 7u;
public static const uint VOXEL_COUNTER_CANDIDATE_INSTANCES = 8u;
public static const uint VOXEL_COUNTER_DISPATCH_CHUNKS = 9u;

[ForceInline]
public uint3 voxel_local_to_toroidal(uint3 localBrick, World3DVoxelCascadeGpu c)
{
    uint mask = c.BricksPerAxis - 1u;
    return uint3(
        (c.ToroidalOriginX + localBrick.x) & mask,
        (c.ToroidalOriginY + localBrick.y) & mask,
        (c.ToroidalOriginZ + localBrick.z) & mask);
}

[ForceInline]
public uint3 voxel_toroidal_to_local(uint3 toroidalBrick, World3DVoxelCascadeGpu c)
{
    uint mask = c.BricksPerAxis - 1u;
    return uint3(
        (toroidalBrick.x - c.ToroidalOriginX) & mask,
        (toroidalBrick.y - c.ToroidalOriginY) & mask,
        (toroidalBrick.z - c.ToroidalOriginZ) & mask);
}

[ForceInline]
public uint voxel_linear_slot(uint cascade, uint3 brick, uint bpa)
{
    return ((cascade * bpa + brick.z) * bpa + brick.y) * bpa + brick.x;
}

[ForceInline]
public uint3 voxel_decode_slot(uint slot, uint bpa, out uint cascade)
{
    uint perCascade = bpa * bpa * bpa;
    cascade = slot / perCascade;
    uint rem = slot - cascade * perCascade;
    uint x = rem % bpa;
    uint y = (rem / bpa) % bpa;
    uint z = rem / (bpa * bpa);
    return uint3(x, y, z);
}

[ForceInline]
public void voxel_mask_address(uint cascade, uint3 localBrick, uint bpa, out uint wordIndex, out uint bitMask)
{
    uint bitIndex = voxel_linear_slot(cascade, localBrick, bpa);
    wordIndex = bitIndex >> 5;
    bitMask = 1u << (bitIndex & 31u);
}

[ForceInline]
public bool voxel_test_local_row(GPUBufferView maskView, uint cascade, int minX, int maxX, uint y, uint z, uint bpa)
{
    uint bitStart = (((cascade * bpa + z) * bpa + y) * bpa) + uint(minX);
    uint wordIdx = bitStart >> 5;
    uint shift = bitStart & 31u;
    uint width = uint(maxX - minX) + 1u;
    uint rowMask = (width >= 32u ? 0xFFFFFFFFu : ((1u << width) - 1u)) << shift;
    return (gpu_load<uint>(maskView, wordIdx) & rowMask) != 0u;
}

[ForceInline]
public void voxel_or_local_row(GPUBufferView maskView, uint cascade, int minX, int maxX, uint y, uint z, uint bpa)
{
    uint bitStart = (((cascade * bpa + z) * bpa + y) * bpa) + uint(minX);
    uint wordIdx = bitStart >> 5;
    uint shift = bitStart & 31u;
    uint width = uint(maxX - minX) + 1u;
    uint rowMask = (width >= 32u ? 0xFFFFFFFFu : ((1u << width) - 1u)) << shift;
    gpu_atomic_or_u32(gpu_offset_bytes(maskView, wordIdx * 4u), rowMask);
}

[ForceInline]
public uint voxel_rgb9e5_encode(float3 rgb)
{
    float3 c = clamp(rgb, float3(0.0), float3(65408.0));
    float maxC = max(c.r, max(c.g, c.b));
    if (maxC < 1e-7)
        return 0u;

    int expShared = max(-15, int(floor(log2(maxC))) + 1);
    float denom = exp2(float(expShared - 9));
    if (int(floor(maxC / denom + 0.5)) >= 512)
    {
        ++expShared;
        denom *= 2.0;
    }

    uint3 m = min(uint3(floor(c / denom + 0.5)), uint3(511u));
    uint biasedExp = uint(clamp(expShared + 15, 0, 31));
    return m.r | (m.g << 9) | (m.b << 18) | (biasedExp << 27);
}

[ForceInline]
public float3 voxel_rgb9e5_decode(uint packed)
{
    if (packed == 0u)
        return float3(0.0);
    int expShared = int((packed >> 27) & 0x1Fu) - 15 - 9;
    float scale = exp2(float(expShared));
    return float3(packed & 0x1FFu, (packed >> 9) & 0x1FFu, (packed >> 18) & 0x1FFu) * scale;
}

[ForceInline]
public bool voxel_decode_accum(
    uint4 packed,
    out float3 albedo,
    out float3 normalWS,
    out float3 premultipliedEmissive,
    out float opacity)
{
    uint rawCount = packed.w & 0xFFFFu;
    if (rawCount == 0u)
    {
        albedo = float3(0.0);
        normalWS = float3(0.0);
        premultipliedEmissive = float3(0.0);
        opacity = 0.0;
        return false;
    }

    float nColor = float(min(rawCount, 31u));
    float invN = rcp(nColor);

    float3 aSum = float3(packed.x & 0x3FFu, (packed.x >> 10) & 0x3FFu, (packed.x >> 20) & 0x3FFu);
    albedo = saturate(aSum * (invN / 31.0));

    float3 nSum = float3(packed.y & 0x3FFu, (packed.y >> 10) & 0x3FFu, (packed.y >> 20) & 0x3FFu);
    float3 nVec = nSum * (invN / 31.0) * 2.0 - 1.0;
    float nLenSq = dot(nVec, nVec);
    normalWS = nLenSq > 1e-4 ? nVec * rsqrt(nLenSq) : float3(0.0);

    premultipliedEmissive = voxel_rgb9e5_decode(packed.z);

    uint opSum = packed.w >> 16;
    opacity = saturate(float(opSum) * (invN / 15.0));
    return true;
}

[ForceInline]
public uint3 voxel_texel_coord(uint3 toroidalBrick, uint localVoxelLinear)
{
    uint vx = localVoxelLinear & 7u;
    uint vy = (localVoxelLinear >> 3) & 7u;
    uint vz = localVoxelLinear >> 6;
    return toroidalBrick * WORLD3D_VOXEL_BRICK_SIZE + uint3(vx, vy, vz);
}
