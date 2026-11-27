#language slang 2025
module World3DVoxelInit;

import Core;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelInitRoot
{
    public GPUBufferView PoolState;
    public GPUBufferView StaticFreeList;
    public GPUBufferView StaticPageTable;
    public GPUBufferView StaticReverseTable;
    public GPUBufferView StaticCounters;
    public GPUBufferView StaticPropBuffer;
    public uint StaticCapacity;
    public uint TotalStaticSlots;
    public uint ItemCount;
    public uint Budget0;
    public uint Budget1;
    public uint Budget2;
    public uint Budget3;
    public uint Reserved0;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VoxelInitRoot root = gpu_load_root<VoxelInitRoot>();
    uint idx = id.x;
    if (idx >= root.ItemCount)
        return;

    if (idx < root.StaticCapacity)
    {
        gpu_store<uint>(root.StaticFreeList, idx, idx);
        gpu_store<uint>(root.StaticReverseTable, idx, WORLD3D_VOXEL_INVALID_INDEX);
    }

    if (idx < root.TotalStaticSlots)
        gpu_store<uint>(root.StaticPageTable, idx, WORLD3D_VOXEL_BRICK_UNCHECKED);

    if (idx == 0u)
    {
        gpu_store<uint>(root.PoolState, VOXEL_STATE_FREE_TOP, root.StaticCapacity);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_FREE_TOP_SNAP, root.StaticCapacity);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_DYNAMIC_CURSOR, 0u);
        gpu_store<uint>(root.PoolState, 3u, 0u);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_CASCADE_ALLOC_BASE + 0u, 0u);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_CASCADE_ALLOC_BASE + 1u, 0u);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_CASCADE_ALLOC_BASE + 2u, 0u);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_CASCADE_ALLOC_BASE + 3u, 0u);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_CASCADE_BUDGET_BASE + 0u, root.Budget0);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_CASCADE_BUDGET_BASE + 1u, root.Budget1);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_CASCADE_BUDGET_BASE + 2u, root.Budget2);
        gpu_store<uint>(root.PoolState, VOXEL_STATE_CASCADE_BUDGET_BASE + 3u, root.Budget3);

        for (uint c = 0u; c < 10u; ++c)
            gpu_store<uint>(root.StaticCounters, c, 0u);
    }
}
