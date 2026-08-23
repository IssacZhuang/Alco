// file "voxel-mip-chain.slang" - module "voxel_mip_chain"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VoxelMipChain.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module voxel_mip_chain;

import alco_rendering_core;
import alco_world3d_voxel_common;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// Cascades three mip transitions (srcMip → srcMip+1 → srcMip+2 → srcMip+3) in
// a single dispatch per texture (radiance or opacity), eliminating the dispatch
// overhead for the tiny tail of the mip chain. Thread (x,y,z) in the 4³ group
// maps to texel (x,y,z) at mip srcMip+1; each thread loads 8 children from the
// source mip and reduces them. Subsequent mips are produced entirely from
// groupshared memory with GroupMemoryBarrierWithGroupSync between levels.
//
// Dispatch: 4 × 4 × (4 * LevelCount) threads — each group handles one clipmap
// level (SV_GroupID.z). Radiance and opacity are processed in separate
// dispatches, selected by push constant mode, because the 8-descriptor-set limit
// cannot accommodate 3 output mips for both textures simultaneously.

struct VoxelMipChainConstants
{
    float4 params; // x=srcMip, y=mode(0=radiance,1=opacity), zw=unused
};

[[vk::binding(0, 1)]] Texture3D _srcTex;
[[vk::binding(0, 2)]] [[vk::image_format("rgba16f")]] RWTexture3D<float4> _outTex1; // mip srcMip+1
[[vk::binding(0, 3)]] [[vk::image_format("rgba16f")]] RWTexture3D<float4> _outTex2; // mip srcMip+2
[[vk::binding(0, 4)]] [[vk::image_format("rgba16f")]] RWTexture3D<float4> _outTex3; // mip srcMip+3

[[vk::push_constant]] VoxelMipChainConstants constants;

// groupshared intermediate data: one float4 per texel at the corresponding mip.
groupshared float4 gsMip1[64]; // 4³ texels at mip srcMip+1
groupshared float4 gsMip2[8];  // 2³ texels at mip srcMip+2

// Occupancy-weighted radiance average (matches VoxelMip.hlsl).
float4 ReduceRadiance(float4 c[8])
{
    float3 radianceSum = 0.0;
    float radianceWeight = 0.0;
    float occupancySum = 0.0;
    [unroll]
    for (uint i = 0u; i < 8u; i++)
    {
        radianceSum += c[i].rgb * c[i].a;
        radianceWeight += c[i].a;
        occupancySum += c[i].a;
    }
    float3 radiance = radianceWeight > 0.0 ? radianceSum / radianceWeight : 0.0;
    return float4(radiance, occupancySum * 0.125);
}

// Projected-area opacity reduction (matches VoxelMip.hlsl).
float4 ReduceOpacity(float4 c[8])
{
    float coverageSum = 0.0;
    [unroll]
    for (uint i = 0u; i < 8u; i++)
    {
        coverageSum += c[i].a;
    }

    float opacityX = 0.0;
    float opacityY = 0.0;
    float opacityZ = 0.0;
    [unroll]
    for (uint a = 0u; a < 2u; a++)
    {
        [unroll]
        for (uint b = 0u; b < 2u; b++)
        {
            opacityX += max(c[0u + a * 2u + b * 4u].x, c[1u + a * 2u + b * 4u].x);
            opacityY += max(c[a + 0u * 2u + b * 4u].y, c[a + 1u * 2u + b * 4u].y);
            opacityZ += max(c[a + b * 2u + 0u * 4u].z, c[a + b * 2u + 1u * 4u].z);
        }
    }
    return float4(float3(opacityX, opacityY, opacityZ) * 0.25, coverageSum * 0.125);
}

float4 Reduce(float4 c[8])
{
    [branch]
    if (constants.params.y < 0.5)
    {
        return ReduceRadiance(c);
    }
    return ReduceOpacity(c);
}

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 gt : SV_GroupThreadID, uint3 groupId : SV_GroupID)
{
    uint resolution = VoxelResolution();
    uint srcMip = (uint)constants.params.x;
    uint level = groupId.z;

    uint srcRes = max(resolution >> srcMip, 1u);
    uint dstRes1 = max(resolution >> (srcMip + 1u), 1u);
    uint dstRes2 = max(resolution >> (srcMip + 2u), 1u);
    uint dstRes3 = max(resolution >> (srcMip + 3u), 1u);

    uint tid = gt.x + gt.y * 4u + gt.z * 16u;

    // --- Transition srcMip → srcMip+1 ---
    // Each thread loads 8 children from the source mip via texture fetches.
    float4 children[8];
    uint3 baseChild = gt * 2u;
    [unroll]
    for (uint dz = 0u; dz <= 1u; dz++)
    {
        [unroll]
        for (uint dy = 0u; dy <= 1u; dy++)
        {
            [unroll]
            for (uint dx = 0u; dx <= 1u; dx++)
            {
                uint3 cc = min(baseChild + uint3(dx, dy, dz), srcRes - 1u);
                uint3 loadCoord = uint3(cc.x, cc.y, level * srcRes + cc.z);
                children[dx + dy * 2u + dz * 4u] = LOAD_TEX3D(_srcTex, loadCoord, 0);
            }
        }
    }

    float4 m1 = Reduce(children);
    gsMip1[tid] = m1;
    uint3 s1 = uint3(gt.x, gt.y, level * dstRes1 + gt.z);
    _outTex1[s1] = m1;

    GroupMemoryBarrierWithGroupSync();

    // --- Transition srcMip+1 → srcMip+2 ---
    // Only threads at even positions participate (8 of 64).
    if ((gt.x & 1u) == 0u && (gt.y & 1u) == 0u && (gt.z & 1u) == 0u)
    {
        [unroll]
        for (uint dz2 = 0u; dz2 <= 1u; dz2++)
        {
            [unroll]
            for (uint dy2 = 0u; dy2 <= 1u; dy2++)
            {
                [unroll]
                for (uint dx2 = 0u; dx2 <= 1u; dx2++)
                {
                    uint3 cc2 = gt + uint3(dx2, dy2, dz2);
                    children[dx2 + dy2 * 2u + dz2 * 4u] =
                        gsMip1[cc2.x + cc2.y * 4u + cc2.z * 16u];
                }
            }
        }

        float4 m2 = Reduce(children);
        uint m2Idx = (gt.x / 2u) + (gt.y / 2u) * 2u + (gt.z / 2u) * 4u;
        gsMip2[m2Idx] = m2;
        uint3 s2 = uint3(gt.x / 2u, gt.y / 2u, level * dstRes2 + gt.z / 2u);
        _outTex2[s2] = m2;
    }

    GroupMemoryBarrierWithGroupSync();

    // --- Transition srcMip+2 → srcMip+3 ---
    // Only thread (0,0,0) participates.
    if (all(gt == uint3(0u, 0u, 0u)))
    {
        [unroll]
        for (uint dz3 = 0u; dz3 <= 1u; dz3++)
        {
            [unroll]
            for (uint dy3 = 0u; dy3 <= 1u; dy3++)
            {
                [unroll]
                for (uint dx3 = 0u; dx3 <= 1u; dx3++)
                {
                    children[dx3 + dy3 * 2u + dz3 * 4u] =
                        gsMip2[dx3 + dy3 * 2u + dz3 * 4u];
                }
            }
        }

        float4 m3 = Reduce(children);
        uint3 s3 = uint3(0u, 0u, level * dstRes3);
        _outTex3[s3] = m3;
    }
}
