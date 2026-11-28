#language slang 2025
module World3DVoxelClear;

import Core;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelClearRoot
{
    public GPUBufferView ActiveBrickList;
    public GPUBufferView BrickPool;
    public GPUBufferView Counters;
    public GPUTextureId VolumeTex;
    public uint Mode; // 0 = clear pool bricks, 1 = clear 3D radiance volume mip0
    public uint BrickLimitCounterOffset;
    public uint BrickCapacity;
    public uint VolumeResolution;
};

[shader("compute")]
[numthreads(4, 4, 4)]
public void csMain(
    uint3 id : SV_DispatchThreadID,
    uint3 groupId : SV_GroupID,
    uint3 localId : SV_GroupThreadID)
{
    VoxelClearRoot root = gpu_load_root<VoxelClearRoot>();
    if (root.Mode == 0u)
    {
        uint activeCount = min(gpu_load<uint>(root.Counters, root.BrickLimitCounterOffset), root.BrickCapacity);
        if (groupId.x >= activeCount)
            return;

        uint2 entry = gpu_load<uint2>(root.ActiveBrickList, groupId.x);
        uint poolIdx = entry.x;
        if (poolIdx >= root.BrickCapacity)
            return;

        uint linearLocal = localId.x + localId.y * 4u + localId.z * 16u;
        uint baseVoxel = poolIdx * WORLD3D_VOXEL_BRICK_UINT4_STRIDE;
        for (uint k = 0u; k < 8u; ++k)
            gpu_store<uint4>(root.BrickPool, baseVoxel + linearLocal * 8u + k, uint4(0u, 0u, 0u, 0u));
        return;
    }

    uint res = root.VolumeResolution;
    if (id.x >= res || id.y >= res || id.z >= res)
        return;

    RWTexture3D<float4> vol = gpu_texture<RWTexture3D<float4>>(root.VolumeTex.index);
    vol[id] = float4(0.0, 0.0, 0.0, 0.0);
}
