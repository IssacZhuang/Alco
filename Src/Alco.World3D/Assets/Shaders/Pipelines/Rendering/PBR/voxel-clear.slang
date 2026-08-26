// file "voxel-clear.slang" - module "voxel_clear"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VoxelClear.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
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

// alco_world3d_voxel_common declares data; this pass's ParameterBlock follows it in
// declaration order.
public struct PassParams
{
    public RWStructuredBuffer<uint4> attrOut;
    public RWStructuredBuffer<uint4> dirtyBricks;
    public RWStructuredBuffer<uint> pageTable;
};

public ParameterBlock<PassParams> pass;


[[vk::push_constant]] VoxelClearConstants constants;

[shader("compute")]
[numthreads(4, 4, 4)]
void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = voxelResolution();
    int level = (int)constants.params.x;
    uint brickIndex = dispatchId.z / VOXEL_BRICK_SIZE;
    uint localZ = dispatchId.z % VOXEL_BRICK_SIZE;
    uint3 logicalCoord = pass.dirtyBricks[brickIndex].xyz * VOXEL_BRICK_SIZE
        + uint3(dispatchId.x, dispatchId.y, localZ);
    if (any(logicalCoord >= resolution))
    {
        return;
    }

    uint pageEntry = pass.pageTable[voxelPageTableSlot(logicalCoord, resolution, level)];
    if (pageEntry == 0u)
    {
        return;
    }
    pass.attrOut[voxelAttributeIndex(pageEntry, logicalCoord)] = uint4(0u, 0u, 0u, 0u);
}
