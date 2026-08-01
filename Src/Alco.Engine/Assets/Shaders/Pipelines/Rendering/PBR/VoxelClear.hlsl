#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Clears one voxel attribute buffer (static or dynamic) of one clipmap level.
// Dispatched at (resolution, resolution, resolution) before voxelization.

DEFINE_STORAGE(1, uint2, _attrOut);

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = VoxelResolution();
    if (any(dispatchId >= resolution))
    {
        return;
    }
    _attrOut[VoxelIndex(dispatchId, resolution)] = uint2(0u, 0u);
}
