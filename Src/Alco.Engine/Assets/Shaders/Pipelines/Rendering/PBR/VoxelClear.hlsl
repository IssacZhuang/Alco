#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Clears resident physical pages for an uploaded logical brick list. Missing
// page-table entries need no work and are skipped.

struct VoxelClearConstants
{
    float4 params; // x=levelIndex
};

DEFINE_STORAGE(1, uint2, _attrOut);
DEFINE_STORAGE(2, uint4, _dirtyBricks);
DEFINE_STORAGE(3, uint, _pageTable);

PUSH_CONSTANT VoxelClearConstants constants;

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = VoxelResolution();
    int level = (int)constants.params.x;
    uint brickIndex = dispatchId.z / VOXEL_BRICK_SIZE;
    uint localZ = dispatchId.z % VOXEL_BRICK_SIZE;
    uint3 logicalCoord = _dirtyBricks[brickIndex].xyz * VOXEL_BRICK_SIZE
        + uint3(dispatchId.x, dispatchId.y, localZ);
    if (any(logicalCoord >= resolution))
    {
        return;
    }

    uint pageEntry = _pageTable[VoxelPageTableSlot(logicalCoord, resolution, level)];
    if (pageEntry == 0u)
    {
        return;
    }
    _attrOut[VoxelAttributeIndex(pageEntry, logicalCoord)] = uint2(0u, 0u);
}
