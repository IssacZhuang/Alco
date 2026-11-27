#language slang 2025
module World3DVoxelPrepareArgs;

import Core;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelPrepareArgsRoot
{
    public GPUBufferView PoolState;
    public GPUBufferView Counters;
    public GPUBufferView ClearStaticArgs;
    public GPUBufferView ClearDynamicArgs;
    public GPUBufferView ExpandChunksArgs;
    public GPUBufferView VoxelizeArgs;
    public GPUBufferView ReclaimEmptyArgs;
    public GPUBufferView InjectDynamicArgs;
    public uint Mode;
    public uint StaticCapacity;
    public uint DynamicCapacity;
    public uint MaxCandidates;
    public uint MaxChunks;
    public uint Reserved0;
    public uint Reserved1;
    public uint Reserved2;
};

[shader("compute")]
[numthreads(1, 1, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VoxelPrepareArgsRoot root = gpu_load_root<VoxelPrepareArgsRoot>();
    if (id.x != 0u)
        return;

    if (root.Mode == 0u)
    {
        uint dirtyStatic = gpu_load<uint>(root.Counters, VOXEL_COUNTER_STATIC_DIRTY_DISPATCHED);
        uint dynCursor = gpu_load<uint>(root.PoolState, VOXEL_STATE_DYNAMIC_CURSOR);
        uint dynUsed = min(dynCursor, root.DynamicCapacity);
        uint cands = min(gpu_load<uint>(root.Counters, VOXEL_COUNTER_CANDIDATE_INSTANCES), root.MaxCandidates);

        gpu_store<uint>(root.Counters, VOXEL_COUNTER_DYNAMIC_USED, dynUsed);
        gpu_store<uint>(root.Counters, VOXEL_COUNTER_CANDIDATE_INSTANCES, cands);

        gpu_store<uint4>(root.ClearStaticArgs, 0u, uint4(dirtyStatic, 1u, 1u, 0u));
        gpu_store<uint4>(root.ClearDynamicArgs, 0u, uint4(dynUsed, 1u, 1u, 0u));
        gpu_store<uint4>(root.ExpandChunksArgs, 0u, uint4(cands, 1u, 1u, 0u));
        gpu_store<uint4>(root.InjectDynamicArgs, 0u, uint4(dynUsed, 1u, 1u, 0u));
        return;
    }

    if (root.Mode == 1u)
    {
        uint chunks = min(gpu_load<uint>(root.Counters, VOXEL_COUNTER_DISPATCH_CHUNKS), root.MaxChunks);
        uint dirtyStatic = gpu_load<uint>(root.Counters, VOXEL_COUNTER_STATIC_DIRTY_DISPATCHED);
        gpu_store<uint>(root.Counters, VOXEL_COUNTER_DISPATCH_CHUNKS, chunks);
        gpu_store<uint4>(root.VoxelizeArgs, 0u, uint4(chunks, 1u, 1u, 0u));
        gpu_store<uint4>(root.ReclaimEmptyArgs, 0u, uint4(dirtyStatic, 1u, 1u, 0u));
        return;
    }

    // Mode == 2: Finalize static used bricks after ReclaimEmptyDirty push.
    uint freeTop = min(gpu_load<uint>(root.PoolState, VOXEL_STATE_FREE_TOP), root.StaticCapacity);
    gpu_store<uint>(root.Counters, VOXEL_COUNTER_STATIC_USED, root.StaticCapacity - freeTop);
}
