#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Radiance + opacity mip downsample for the voxel GI clipmap: one dispatch per
// level per mip transition at (dstRes, dstRes, dstRes). Averages the 8 child
// voxels: radiance with occupancy weighting (occupancy becomes the fraction of
// occupied children), and directional opacity as a plain average (thin walls
// in coarser voxels naturally get lower opacity).
// All levels share the one Texture3D, stacked along the w axis; the child mip
// is read with exact texel fetches, the parent mip written through the bound
// single-mip storage view.

struct VoxelMipConstants
{
    float4 params; // x=mipIndex, y=levelIndex, zw=unused
};

DEFINE_TEX3D_READ(1, _radianceLoad);
DEFINE_TEX3D_STORAGE(2, _radianceOut, float4, "rgba16f");
DEFINE_TEX3D_READ(3, _opacityLoad);
DEFINE_TEX3D_STORAGE(4, _opacityOut, float4, "rgba16f");

PUSH_CONSTANT VoxelMipConstants constants;

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = VoxelResolution();
    uint mip = (uint)constants.params.x;
    uint level = (uint)constants.params.y;
    uint srcRes = max(resolution >> mip, 1u);
    uint dstRes = max(resolution >> (mip + 1u), 1u);
    if (any(dispatchId >= dstRes))
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
    float3 opacitySum = 0.0;
    float opacityWeightSum = 0.0;
    for (uint dz = 0; dz <= 1; dz++)
    {
        for (uint dy = 0; dy <= 1; dy++)
        {
            for (uint dx = 0; dx <= 1; dx++)
            {
                uint3 coord = min(dispatchId * 2 + uint3(dx, dy, dz), srcRes - 1);
                uint3 loadCoord = uint3(coord.x, coord.y, level * srcRes + coord.z);
                float4 radSample = LOAD_TEX3D(_radianceLoad, loadCoord, 0);
                if (radSample.a > 0.01)
                {
                    radianceSum += radSample.rgb;
                    radianceWeight += 1.0;
                }
                occupancySum += radSample.a;

                float4 opaSample = LOAD_TEX3D(_opacityLoad, loadCoord, 0);
                float opaWeight = step(0.01, opaSample.a);
                opacitySum += opaSample.xyz * opaWeight;
                opacityWeightSum += opaWeight;
            }
        }
    }

    uint3 storeCoord = uint3(dispatchId.x, dispatchId.y, level * dstRes + dispatchId.z);
    float3 radiance = radianceWeight > 0.0 ? radianceSum / radianceWeight : 0.0;
    _radianceOut[storeCoord] = float4(radiance, occupancySum / 8.0);

    float3 opacity = opacityWeightSum > 0.0 ? opacitySum / opacityWeightSum : 0.0;
    _opacityOut[storeCoord] = float4(opacity, opacityWeightSum / 8.0);
}
