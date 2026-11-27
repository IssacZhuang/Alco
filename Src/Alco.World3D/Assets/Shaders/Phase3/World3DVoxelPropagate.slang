#language slang 2025
module World3DVoxelPropagate;

import Core;
import SharedSampler;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelPropagateRoot
{
    public GPUBufferView Cascades;
    public GPUBufferView ReverseTable;
    public GPUBufferView BrickPool;
    public GPUBufferView PropBrickBuffer;
    public GPUBufferView Counters;
    public GPUTextureId Vol0;
    public GPUTextureId Vol1;
    public GPUTextureId Vol2;
    public GPUTextureId Vol3;
    public float PropagationDecay;
    public uint Mode; // 0 = Gather indirect bounce into PropBrickBuffer, 1 = Add PropBrickBuffer to Vol0..3
    public uint DynamicMode;
    public uint BricksPerAxis;
    public uint PoolCapacity;
    public uint Reserved0;
    public uint Reserved1;
    public uint Reserved2;
};

float4 sampleCascade(VoxelPropagateRoot root, uint c, float3 uvw, float mip)
{
    SamplerState s = shared_sampler_linear_repeat();
    if (c == 0u) return gpu_texture<Texture3D<float4>>(root.Vol0.index).SampleLevel(s, uvw, mip);
    if (c == 1u) return gpu_texture<Texture3D<float4>>(root.Vol1.index).SampleLevel(s, uvw, mip);
    if (c == 2u) return gpu_texture<Texture3D<float4>>(root.Vol2.index).SampleLevel(s, uvw, mip);
    return gpu_texture<Texture3D<float4>>(root.Vol3.index).SampleLevel(s, uvw, mip);
}

float4 readVolume(VoxelPropagateRoot root, uint c, uint3 coord)
{
    if (c == 0u) return gpu_texture<RWTexture3D<float4>>(root.Vol0.index)[coord];
    if (c == 1u) return gpu_texture<RWTexture3D<float4>>(root.Vol1.index)[coord];
    if (c == 2u) return gpu_texture<RWTexture3D<float4>>(root.Vol2.index)[coord];
    return gpu_texture<RWTexture3D<float4>>(root.Vol3.index)[coord];
}

void writeVolume(VoxelPropagateRoot root, uint c, uint3 coord, float4 val)
{
    if (c == 0u) gpu_texture<RWTexture3D<float4>>(root.Vol0.index)[coord] = val;
    else if (c == 1u) gpu_texture<RWTexture3D<float4>>(root.Vol1.index)[coord] = val;
    else if (c == 2u) gpu_texture<RWTexture3D<float4>>(root.Vol2.index)[coord] = val;
    else gpu_texture<RWTexture3D<float4>>(root.Vol3.index)[coord] = val;
}

float3 gatherBounceCone(VoxelPropagateRoot root, uint startCascade, float3 originAnchorRel, float3 dir, float tanHalfAngle)
{
    float voxelSize0 = gpu_load<World3DVoxelCascadeGpu>(root.Cascades, startCascade).OriginVoxelSize.w;
    float3 origin = originAnchorRel + dir * (voxelSize0 * 1.65);

    float3 accumRgb = float3(0.0);
    float accumA = 0.0;
    float t = voxelSize0 * 1.25;

    for (uint stepIdx = 0u; stepIdx < 5u && accumA < 0.96; ++stepIdx)
    {
        float diam = max(voxelSize0, 2.0 * tanHalfAngle * t);
        float3 samplePos = origin + dir * t;

        uint c = startCascade;
        bool inside = false;
        for (; c < WORLD3D_VOXEL_CASCADE_COUNT; ++c)
        {
            World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(root.Cascades, c);
            float res = float(cGpu.BricksPerAxis * WORLD3D_VOXEL_BRICK_SIZE);
            float3 localV = (samplePos - cGpu.OriginVoxelSize.xyz) / cGpu.OriginVoxelSize.w;
            if (all(localV >= 1.5 && localV <= (res - 1.5)))
            {
                float mip = clamp(log2(max(diam / cGpu.OriginVoxelSize.w, 1.0)), 0.0, 4.0);
                float3 toroidalV = localV + float3(cGpu.ToroidalOriginX, cGpu.ToroidalOriginY, cGpu.ToroidalOriginZ) * float(WORLD3D_VOXEL_BRICK_SIZE);
                float4 tap = sampleCascade(root, c, toroidalV / res, mip);
                float w = 1.0 - accumA;
                accumRgb += tap.rgb * w;
                accumA += tap.a * w;
                inside = true;
                break;
            }
        }
        if (!inside)
            break;
        t += max(diam * 0.85, voxelSize0 * 1.5);
    }
    return accumRgb;
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID)
{
    VoxelPropagateRoot root = gpu_load_root<VoxelPropagateRoot>();
    uint poolIdx = groupId.x;
    if (poolIdx >= root.PoolCapacity)
        return;

    if (root.DynamicMode != 0u)
    {
        uint dynUsed = min(gpu_load<uint>(root.Counters, VOXEL_COUNTER_DYNAMIC_USED), root.PoolCapacity);
        if (poolIdx >= dynUsed)
            return;
    }

    uint pageSlot = gpu_load<uint>(root.ReverseTable, poolIdx);
    if (pageSlot == WORLD3D_VOXEL_INVALID_INDEX)
        return;

    uint cascade;
    uint3 toroidalBrick = voxel_decode_slot(pageSlot, root.BricksPerAxis, cascade);
    if (cascade >= WORLD3D_VOXEL_CASCADE_COUNT)
        return;

    World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(root.Cascades, cascade);
    uint3 localBrick = voxel_toroidal_to_local(toroidalBrick, cGpu);
    float voxelSize = cGpu.OriginVoxelSize.w;
    uint baseVoxel = poolIdx * WORLD3D_VOXEL_BRICK_UINT4_STRIDE;

    if (root.Mode == 1u)
    {
        for (uint k = 0u; k < 8u; ++k)
        {
            uint v = localId.x * 8u + k;
            uint packedBounce = gpu_load<uint>(root.PropBrickBuffer, baseVoxel + v);
            if (packedBounce == 0u)
                continue;
            uint3 subVoxel = uint3(v & 7u, (v >> 3) & 7u, v >> 6);
            uint3 texelCoord = toroidalBrick * WORLD3D_VOXEL_BRICK_SIZE + subVoxel;
            float4 cur = readVolume(root, cascade, texelCoord);
            cur.rgb += voxel_rgb9e5_decode(packedBounce);
            writeVolume(root, cascade, texelCoord, cur);
        }
        return;
    }

    static const float3 coneDirs[5] = {
        float3(0.0, 0.0, 1.0),
        float3(0.70710678, 0.0, 0.70710678),
        float3(-0.70710678, 0.0, 0.70710678),
        float3(0.0, 0.70710678, 0.70710678),
        float3(0.0, -0.70710678, 0.70710678),
    };
    static const float coneWeights[5] = { 0.28, 0.18, 0.18, 0.18, 0.18 };

    for (uint k = 0u; k < 8u; ++k)
    {
        uint v = localId.x * 8u + k;
        uint4 packed = gpu_load<uint4>(root.BrickPool, baseVoxel + v);
        float3 albedo;
        float3 normalWS;
        float3 emissive;
        float opacity;
        if (!voxel_decode_accum(packed, albedo, normalWS, emissive, opacity))
        {
            gpu_store<uint>(root.PropBrickBuffer, baseVoxel + v, 0u);
            continue;
        }

        uint3 subVoxel = uint3(v & 7u, (v >> 3) & 7u, v >> 6);
        float3 anchorRelPos = cGpu.OriginVoxelSize.xyz + (float3(localBrick * WORLD3D_VOXEL_BRICK_SIZE + subVoxel) + 0.5) * voxelSize;

        float3 n = dot(normalWS, normalWS) > 0.01 ? normalWS : float3(0.0, 0.0, 1.0);
        float3 upVec = abs(n.z) < 0.99 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
        float3 t = normalize(cross(upVec, n));
        float3 b = cross(n, t);

        float3 irradiance = float3(0.0);
        [unroll]
        for (uint ci = 0u; ci < 5u; ++ci)
        {
            float3 localDir = coneDirs[ci];
            float3 worldDir = t * localDir.x + b * localDir.y + n * localDir.z;
            irradiance += gatherBounceCone(root, cascade, anchorRelPos, worldDir, 0.577) * coneWeights[ci];
        }

        float3 bounceRad = albedo * irradiance * (root.PropagationDecay * opacity);
        gpu_store<uint>(root.PropBrickBuffer, baseVoxel + v, voxel_rgb9e5_encode(bounceRad));
    }
}
