// file "VoxelClear.slang" - module "voxel_clear"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VoxelClear.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module voxel_clear;

import alco_rendering_core;
import alco_world3d_voxel_common;

// Clears resident physical pages for an uploaded logical brick list. Missing
// page-table entries need no work and are skipped.

struct VoxelClearConstants
{
    float4 params; // x=levelIndex
};

[[vk::binding(0, 1)]] RWStructuredBuffer<uint4> _attrOut;
[[vk::binding(0, 2)]] RWStructuredBuffer<uint4> _dirtyBricks;
[[vk::binding(0, 3)]] RWStructuredBuffer<uint> _pageTable;

[[vk::push_constant]] VoxelClearConstants constants;

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
    _attrOut[VoxelAttributeIndex(pageEntry, logicalCoord)] = uint4(0u, 0u, 0u, 0u);
}
