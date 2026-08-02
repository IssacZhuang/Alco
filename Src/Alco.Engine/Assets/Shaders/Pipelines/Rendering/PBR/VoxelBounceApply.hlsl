#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Copies the propagate result (direct + bounce radiance) back into the
// radiance Texture3D mip 0. One dispatch per clipmap level at
// (resolution, resolution, resolution). The source (_propagateLoad) and
// destination (_radianceOut) are different textures, so there is no
// read/write hazard.

struct VoxelBounceApplyConstants
{
    float4 params; // x=levelIndex, yzw=unused
};

DEFINE_TEX3D_READ(1, _propagateLoad);
DEFINE_TEX3D_STORAGE(2, _radianceOut, float4, "rgba16f");

PUSH_CONSTANT VoxelBounceApplyConstants constants;

[shader("compute")]
[numthreads(4, 4, 4)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint resolution = VoxelResolution();
    if (any(dispatchId >= resolution))
    {
        return;
    }

    int level = (int)constants.params.x;
    uint3 coord = uint3(dispatchId.x, dispatchId.y, (uint)level * resolution + dispatchId.z);
    _radianceOut[coord] = LOAD_TEX3D(_propagateLoad, coord, 0);
}
