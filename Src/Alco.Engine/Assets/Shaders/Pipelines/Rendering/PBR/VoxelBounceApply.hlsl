#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Copies the propagate result (direct + bounce radiance) back into the
// radiance Texture3D mip 0. Sparse dispatch over a brick list (resident bricks
// only). The source (_propagateLoad) and destination (_radianceOut) are
// different textures, so there is no read/write hazard.

struct VoxelBounceApplyConstants
{
    float4 params; // x=levelIndex, yzw=unused
};

DEFINE_TEX3D_READ(1, _propagateLoad);
DEFINE_TEX3D_STORAGE(2, _radianceOut, float4, "rgba16f");
DEFINE_STORAGE(3, uint4, _brickList);

PUSH_CONSTANT VoxelBounceApplyConstants constants;

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = VoxelResolution();
    int level = (int)constants.params.x;
    uint brickIndex = dispatchId.z / VOXEL_BRICK_SIZE;
    uint localZ = dispatchId.z % VOXEL_BRICK_SIZE;
    uint3 logicalCoord = _brickList[brickIndex].xyz * VOXEL_BRICK_SIZE
        + uint3(dispatchId.x, dispatchId.y, localZ);
    if (any(logicalCoord >= resolution))
    {
        return;
    }

    uint3 coord = uint3(logicalCoord.x, logicalCoord.y, (uint)level * resolution + logicalCoord.z);
    _radianceOut[coord] = LOAD_TEX3D(_propagateLoad, coord, 0);
}
