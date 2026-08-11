#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/RadianceCacheCommon.hlsli"

// Clears only the per-frame fixed-point accumulation buffers. The persistent
// floating-point cache is reprojected and updated by RadianceCacheUpdate.

DEFINE_STORAGE(1, uint, _accumRadiance);
DEFINE_STORAGE(1, uint, _accumNormal);

[shader("compute")]
[numthreads(256, 1, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint index = dispatchId.x;
    if (index >= (uint)cacheParams.z)
    {
        return;
    }
    uint baseIndex = index * 4u;
    _accumRadiance[baseIndex + 0u] = 0u;
    _accumRadiance[baseIndex + 1u] = 0u;
    _accumRadiance[baseIndex + 2u] = 0u;
    _accumRadiance[baseIndex + 3u] = 0u;
    _accumNormal[baseIndex + 0u] = 0u;
    _accumNormal[baseIndex + 1u] = 0u;
    _accumNormal[baseIndex + 2u] = 0u;
    _accumNormal[baseIndex + 3u] = 0u;
}
