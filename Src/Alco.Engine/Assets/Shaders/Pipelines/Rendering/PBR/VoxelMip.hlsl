#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Radiance mip downsample for the voxel GI clipmap: one dispatch per level per
// mip transition at (dstRes, dstRes, dstRes). Averages the 8 child voxels with
// occupancy weighting; occupancy becomes the fraction of occupied children.
// All levels share the one radiance Texture3D, stacked along the w axis; the
// child mip is read with exact texel fetches, the parent mip written through
// the bound single-mip storage view.

struct VoxelMipConstants
{
    float4 params; // x=mipIndex, y=levelIndex, zw=unused
};

DEFINE_TEX3D_READ(1, _radianceLoad);
DEFINE_TEX3D_STORAGE(2, _radianceOut, float4, "rgba16f");

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

    float3 radianceSum = 0.0;
    float occupancySum = 0.0;
    for (uint dz = 0; dz <= 1; dz++)
    {
        for (uint dy = 0; dy <= 1; dy++)
        {
            for (uint dx = 0; dx <= 1; dx++)
            {
                uint3 coord = min(dispatchId * 2 + uint3(dx, dy, dz), srcRes - 1);
                uint3 loadCoord = uint3(coord.x, coord.y, level * srcRes + coord.z);
                //the bound single-mip view rebases the child mip to mip 0
                float4 sample_ = LOAD_TEX3D(_radianceLoad, loadCoord, 0);
                radianceSum += sample_.rgb * sample_.a;
                occupancySum += sample_.a;
            }
        }
    }

    float3 radiance = occupancySum > 0.0 ? radianceSum / occupancySum : 0.0;
    uint3 storeCoord = uint3(dispatchId.x, dispatchId.y, level * dstRes + dispatchId.z);
    _radianceOut[storeCoord] = float4(radiance, occupancySum / 8.0);
}
