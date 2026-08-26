// file "voxel-mip.slang" - module "voxel_mip"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VoxelMip.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module voxel_mip;

import alco_rendering_core;
import alco_world3d_voxel_common;

// Radiance + opacity mip downsample for the voxel GI clipmap. One dispatch
// per mip transition covers ALL clipmap levels: the z dimension encodes both
// the texel z (low bits) and the level index (high bits), so dispatching
// (dstRes, dstRes, dstRes * LevelCount) threads processes every level without
// a per-level dispatch call. Radiance is averaged with volume coverage weights.
// Directional opacity uses a projected-area reduction: maximum coverage along
// the projection axis and an average across the orthogonal plane. This preserves
// thin walls at coarse mips, matching the sub-voxel opacity reduction used by
// SVOGI.
// All levels share the one Texture3D, stacked along the w axis; the child mip
// is read with exact texel fetches, the parent mip written through the bound
// single-mip storage view.

struct VoxelMipConstants
{
    float4 params; // x=mipIndex, yzw=unused (level derived from dispatch z)
};

// alco_world3d_voxel_common declares data; this pass's ParameterBlock follows it in
// declaration order.
public struct PassParams
{
    public Texture3D radianceLoad;
    public [[vk::image_format("rgba16f")]] RWTexture3D<float4> radianceOut;
    public Texture3D opacityLoad;
    public [[vk::image_format("rgba16f")]] RWTexture3D<float4> opacityOut;
};

public ParameterBlock<PassParams> pass;


[[vk::push_constant]] VoxelMipConstants constants;

[shader("compute")]
[numthreads(4, 4, 4)]
void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = voxelResolution();
    uint mip = (uint)constants.params.x;
    uint srcRes = max(resolution >> mip, 1u);
    uint dstRes = max(resolution >> (mip + 1u), 1u);

    // Level is packed into the dispatch z dimension: z / dstRes gives the
    // clipmap level, z % dstRes gives the texel z within that level.
    uint level = dispatchId.z / dstRes;
    uint zTexel = dispatchId.z % dstRes;
    if (dispatchId.x >= dstRes || dispatchId.y >= dstRes)
    {
        return;
    }

    // Radiance: average only over occupied children to avoid diluting bright
    // thin geometry when it collapses into a coarser voxel. The occupancy-
    // weighted average (dividing by 8) would dim a lone occupied voxel by up
    // to 8× at each mip level, making distant bounce light nearly invisible.
    float3 radianceSum = 0.0;
    float radianceWeight = 0.0;
    float occupancySum = 0.0;
    float opacityCoverageSum = 0.0;
    float3 opacityChildren[8];
    for (uint dz = 0; dz <= 1; dz++)
    {
        for (uint dy = 0; dy <= 1; dy++)
        {
            for (uint dx = 0; dx <= 1; dx++)
            {
                uint3 coord = min(uint3(dispatchId.x * 2 + dx, dispatchId.y * 2 + dy, zTexel * 2 + dz), srcRes - 1);
                uint3 loadCoord = uint3(coord.x, coord.y, level * srcRes + coord.z);
                float4 radSample = pass.radianceLoad.Load(int4(loadCoord, 0));
                radianceSum += radSample.rgb * radSample.a;
                radianceWeight += radSample.a;
                occupancySum += radSample.a;

                float4 opaSample = pass.opacityLoad.Load(int4(loadCoord, 0));
                opacityChildren[dx + dy * 2u + dz * 4u] = opaSample.xyz;
                opacityCoverageSum += opaSample.a;
            }
        }
    }

    uint3 storeCoord = uint3(dispatchId.x, dispatchId.y, level * dstRes + zTexel);
    float3 radiance = radianceWeight > 0.0 ? radianceSum / radianceWeight : 0.0;
    pass.radianceOut[storeCoord] = float4(radiance, occupancySum / 8.0);

    // Preserve volume coverage at coarse mips. Dividing by only the occupied
    // children makes a single thin surface fully opaque at every mip and causes
    // severe long-range over-occlusion during cone tracing.
    float opacityX = 0.0;
    float opacityY = 0.0;
    float opacityZ = 0.0;
    [unroll]
    for (uint a = 0u; a < 2u; a++)
    {
        [unroll]
        for (uint b = 0u; b < 2u; b++)
        {
            opacityX += max(
                opacityChildren[0u + a * 2u + b * 4u].x,
                opacityChildren[1u + a * 2u + b * 4u].x);
            opacityY += max(
                opacityChildren[a + 0u * 2u + b * 4u].y,
                opacityChildren[a + 1u * 2u + b * 4u].y);
            opacityZ += max(
                opacityChildren[a + b * 2u + 0u * 4u].z,
                opacityChildren[a + b * 2u + 1u * 4u].z);
        }
    }
    float3 projectedOpacity = float3(opacityX, opacityY, opacityZ) * 0.25;
    pass.opacityOut[storeCoord] = float4(projectedOpacity, opacityCoverageSum / 8.0);
}
