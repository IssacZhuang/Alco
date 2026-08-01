#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Radiance mip downsample for the voxel GI clipmap: one dispatch per level per
// mip transition at (dstRes, dstRes, dstRes). Averages the 8 child voxels with
// occupancy weighting; occupancy becomes the fraction of occupied children.
// All levels share the one radiance buffer (see VoxelRadianceLevelStride).

struct VoxelMipConstants
{
    float4 params; // x=mipIndex, y=levelIndex, zw=unused
};

DEFINE_STORAGE(1, uint2, _radiance);

PUSH_CONSTANT VoxelMipConstants constants;

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = VoxelResolution();
    uint mip = (uint)constants.params.x;
    uint srcRes = max(resolution >> mip, 1u);
    uint dstRes = max(resolution >> (mip + 1u), 1u);
    if (any(dispatchId >= dstRes))
    {
        return;
    }

    uint levelStride = (uint)constants.params.y * VoxelRadianceLevelStride(resolution, (uint)clipmapParams.z);
    uint srcOffset = levelStride + VoxelMipOffset(resolution, mip);
    uint dstOffset = levelStride + VoxelMipOffset(resolution, mip + 1u);

    float3 radianceSum = 0.0;
    float occupancySum = 0.0;
    for (uint dz = 0; dz <= 1; dz++)
    {
        for (uint dy = 0; dy <= 1; dy++)
        {
            for (uint dx = 0; dx <= 1; dx++)
            {
                uint3 coord = min(dispatchId * 2 + uint3(dx, dy, dz), srcRes - 1);
                float4 sample_ = UnpackVoxelRadiance(_radiance[srcOffset + VoxelIndex(coord, srcRes)]);
                radianceSum += sample_.rgb * sample_.a;
                occupancySum += sample_.a;
            }
        }
    }

    float3 radiance = occupancySum > 0.0 ? radianceSum / occupancySum : 0.0;
    _radiance[dstOffset + VoxelIndex(dispatchId, dstRes)] = PackVoxelRadiance(radiance, occupancySum / 8.0);
}
