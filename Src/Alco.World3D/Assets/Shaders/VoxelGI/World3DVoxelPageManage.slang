#language slang 2025
module World3DVoxelPageManage;

import Core;
import World3DVoxelCommon;

[GPURoot]
public struct VoxelPageManageRoot
{
    public GPUBufferView Frame;
    public uint Mode;
    public uint ItemCount;
};

groupshared uint sHasData[64];

uint getBudgetForCascade(World3DVoxelFrameGpu frame, uint cascade)
{
    if (cascade == 0u) return frame.Budget0;
    if (cascade == 1u) return frame.Budget1;
    if (cascade == 2u) return frame.Budget2;
    return frame.Budget3;
}

void executeResetAndEvict(World3DVoxelFrameGpu frame, uint idx)
{
    if (idx < frame.StaticMaskWords)
        gpu_store<uint>(frame.StaticDirtyMask, idx, 0u);
    if (idx < frame.DynamicMaskWords)
        gpu_store<uint>(frame.DynamicDesiredMask, idx, 0u);
    if (idx < frame.TotalDynamicSlots)
        gpu_store<uint>(frame.DynamicPageTable, idx, WORLD3D_VOXEL_BRICK_UNCHECKED);
    if (idx < frame.DynamicCapacity)
        gpu_store<uint>(frame.DynamicReverseTable, idx, WORLD3D_VOXEL_INVALID_INDEX);

    if (idx == 0u)
    {
        gpu_store<uint>(frame.PoolState, VOXEL_STATE_DYNAMIC_CURSOR, 0u);
        for (uint c = 0u; c < WORLD3D_VOXEL_CASCADE_COUNT; ++c)
            gpu_store<uint>(frame.PoolState, VOXEL_STATE_CASCADE_ALLOC_BASE + c, 0u);
        gpu_store<uint>(frame.PoolState, VOXEL_STATE_CASCADE_BUDGET_BASE + 0u, frame.Budget0);
        gpu_store<uint>(frame.PoolState, VOXEL_STATE_CASCADE_BUDGET_BASE + 1u, frame.Budget1);
        gpu_store<uint>(frame.PoolState, VOXEL_STATE_CASCADE_BUDGET_BASE + 2u, frame.Budget2);
        gpu_store<uint>(frame.PoolState, VOXEL_STATE_CASCADE_BUDGET_BASE + 3u, frame.Budget3);

        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_ALLOCATED, 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_RECLAIMED, 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_FALLBACK, 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_DIRTY_QUEUED, 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_DIRTY_DISPATCHED, 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_DYNAMIC_USED, 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_DYNAMIC_FALLBACK, 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_CANDIDATE_INSTANCES, 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_DISPATCH_CHUNKS, 0u);
    }

    if (idx >= frame.TotalStaticSlots || frame.EvictBoxCount == 0u)
        return;

    uint cascade;
    uint3 toroidal = voxel_decode_slot(idx, frame.BricksPerAxis, cascade);
    World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, cascade);
    int3 local = int3(voxel_toroidal_to_local(toroidal, cGpu));

    bool evicted = false;
    for (uint b = 0u; b < frame.EvictBoxCount; ++b)
    {
        World3DVoxelEvictBoxGpu box = gpu_load<World3DVoxelEvictBoxGpu>(frame.EvictBoxes, b);
        if (box.CascadeIndex != int(cascade))
            continue;
        if (local.x >= box.MinLocalX && local.x < box.MaxLocalExclX
            && local.y >= box.MinLocalY && local.y < box.MaxLocalExclY
            && local.z >= box.MinLocalZ && local.z < box.MaxLocalExclZ)
        {
            evicted = true;
            break;
        }
    }

    if (!evicted)
        return;

    uint oldEntry = gpu_load<uint>(frame.StaticPageTable, idx);
    if (oldEntry < frame.StaticCapacity)
    {
        uint pushPos = gpu_atomic_add_u32(gpu_offset_bytes(frame.PoolState, VOXEL_STATE_FREE_TOP * 4u), 1u);
        if (pushPos < frame.StaticCapacity)
            gpu_store<uint>(frame.StaticFreeList, pushPos, oldEntry);
        gpu_store<uint>(frame.StaticReverseTable, oldEntry, WORLD3D_VOXEL_INVALID_INDEX);
    }
    gpu_store<uint>(frame.StaticPageTable, idx, WORLD3D_VOXEL_BRICK_UNCHECKED);
}

void executeSelectDirtyStatic(World3DVoxelFrameGpu frame, uint idx)
{
    if (idx == 0u)
        gpu_store<uint>(frame.PoolState, VOXEL_STATE_FREE_TOP_SNAP, gpu_load<uint>(frame.PoolState, VOXEL_STATE_FREE_TOP));

    if (idx >= frame.TotalStaticSlots)
        return;

    if (gpu_load<uint>(frame.StaticPageTable, idx) != WORLD3D_VOXEL_BRICK_UNCHECKED)
        return;

    uint perCascade = frame.BricksPerAxis * frame.BricksPerAxis * frame.BricksPerAxis;
    uint cascade = idx / perCascade;
    uint budget = getBudgetForCascade(frame, cascade);
    uint slotInBudget = gpu_atomic_add_u32(gpu_offset_bytes(frame.PoolState, (VOXEL_STATE_CASCADE_ALLOC_BASE + cascade) * 4u), 1u);
    if (slotInBudget >= budget)
        return;

    uint queuePos = gpu_atomic_add_u32(gpu_offset_bytes(frame.Counters, VOXEL_COUNTER_STATIC_DIRTY_QUEUED * 4u), 1u);
    if (queuePos < frame.StaticCapacity)
        gpu_store<uint>(frame.DirtyQueue, queuePos, idx);
}

void executePopAndBindStaticPages(World3DVoxelFrameGpu frame, uint idx)
{
    uint queued = gpu_load<uint>(frame.Counters, VOXEL_COUNTER_STATIC_DIRTY_QUEUED);
    uint queuedClamped = min(queued, frame.StaticCapacity);
    uint freeTop = min(gpu_load<uint>(frame.PoolState, VOXEL_STATE_FREE_TOP_SNAP), frame.StaticCapacity);
    uint allocCount = min(queuedClamped, freeTop);

    if (idx < allocCount)
    {
        uint pageSlot = gpu_load<uint>(frame.DirtyQueue, idx);
        uint poolIdx = gpu_load<uint>(frame.StaticFreeList, freeTop - 1u - idx);

        gpu_store<uint>(frame.StaticPageTable, pageSlot, poolIdx);
        gpu_store<uint>(frame.StaticReverseTable, poolIdx, pageSlot);
        gpu_store<uint2>(frame.ActiveDirtyList, idx, uint2(poolIdx, pageSlot));

        uint cascade;
        uint3 toroidal = voxel_decode_slot(pageSlot, frame.BricksPerAxis, cascade);
        World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, cascade);
        uint3 local = voxel_toroidal_to_local(toroidal, cGpu);
        uint wordIdx;
        uint bitMask;
        voxel_mask_address(cascade, local, frame.BricksPerAxis, wordIdx, bitMask);
        gpu_atomic_or_u32(gpu_offset_bytes(frame.StaticDirtyMask, wordIdx * 4u), bitMask);
    }

    if (idx == 0u)
    {
        uint remainingFree = freeTop - allocCount;
        gpu_store<uint>(frame.PoolState, VOXEL_STATE_FREE_TOP, remainingFree);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_DIRTY_QUEUED, queuedClamped);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_ALLOCATED, allocCount);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_DIRTY_DISPATCHED, allocCount);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_FALLBACK, queued > allocCount ? (queued - allocCount) : 0u);
        gpu_store<uint>(frame.Counters, VOXEL_COUNTER_STATIC_USED, frame.StaticCapacity - remainingFree);
    }
}

void executeAllocateDynamicPages(World3DVoxelFrameGpu frame, uint idx)
{
    if (idx >= frame.TotalDynamicSlots || frame.DynamicCapacity == 0u)
        return;

    uint cascade;
    uint3 local = voxel_decode_slot(idx, frame.BricksPerAxis, cascade);
    if (cascade >= frame.DynamicLevelCount)
        return;

    uint wordIdx;
    uint bitMask;
    voxel_mask_address(cascade, local, frame.BricksPerAxis, wordIdx, bitMask);
    if ((gpu_load<uint>(frame.DynamicDesiredMask, wordIdx) & bitMask) == 0u)
        return;

    uint dynIdx = gpu_atomic_add_u32(gpu_offset_bytes(frame.PoolState, VOXEL_STATE_DYNAMIC_CURSOR * 4u), 1u);
    if (dynIdx < frame.DynamicCapacity)
    {
        World3DVoxelCascadeGpu cGpu = gpu_load<World3DVoxelCascadeGpu>(frame.Cascades, cascade);
        uint3 toroidal = voxel_local_to_toroidal(local, cGpu);
        uint pageSlot = voxel_linear_slot(cascade, toroidal, frame.BricksPerAxis);
        gpu_store<uint>(frame.DynamicPageTable, pageSlot, dynIdx);
        gpu_store<uint>(frame.DynamicReverseTable, dynIdx, pageSlot);
        gpu_store<uint2>(frame.DynamicActiveList, dynIdx, uint2(dynIdx, pageSlot));
    }
    else
    {
        gpu_atomic_add_u32(gpu_offset_bytes(frame.Counters, VOXEL_COUNTER_DYNAMIC_FALLBACK * 4u), 1u);
    }
}

[shader("compute")]
[numthreads(64, 1, 1)]
public void csMain(
    uint3 id : SV_DispatchThreadID,
    uint3 groupId : SV_GroupID,
    uint3 localId : SV_GroupThreadID)
{
    VoxelPageManageRoot root = gpu_load_root<VoxelPageManageRoot>();
    World3DVoxelFrameGpu frame = gpu_load<World3DVoxelFrameGpu>(root.Frame, 0u);
    if (root.Mode == 0u)
    {
        executeResetAndEvict(frame, id.x);
        return;
    }
    if (root.Mode == 1u)
    {
        executeSelectDirtyStatic(frame, id.x);
        return;
    }
    if (root.Mode == 2u)
    {
        executePopAndBindStaticPages(frame, id.x);
        return;
    }
    if (root.Mode == 3u)
    {
        executeAllocateDynamicPages(frame, id.x);
        return;
    }

    uint dispatched = gpu_load<uint>(frame.Counters, VOXEL_COUNTER_STATIC_DIRTY_DISPATCHED);
    uint active = groupId.x < dispatched ? 1u : 0u;
    uint poolIdx = 0u;
    uint pageSlot = 0u;
    uint nonZero = 0u;
    if (active != 0u)
    {
        uint2 entry = gpu_load<uint2>(frame.ActiveDirtyList, groupId.x);
        poolIdx = entry.x;
        pageSlot = entry.y;
        uint baseVoxel = poolIdx * WORLD3D_VOXEL_BRICK_UINT4_STRIDE;
        for (uint k = 0u; k < 8u; ++k)
        {
            uint v = localId.x * 8u + k;
            uint4 voxel = gpu_load<uint4>(frame.StaticBrickPool, baseVoxel + v);
            if (voxel.w != 0u)
            {
                nonZero = 1u;
                break;
            }
        }
    }
    sHasData[localId.x] = nonZero;
    GroupMemoryBarrierWithGroupSync();

    if (active != 0u && localId.x == 0u)
    {
        uint anyData = 0u;
        for (uint t = 0u; t < 64u; ++t)
            anyData |= sHasData[t];
        if (anyData == 0u)
        {
            uint pushPos = gpu_atomic_add_u32(gpu_offset_bytes(frame.PoolState, VOXEL_STATE_FREE_TOP * 4u), 1u);
            if (pushPos < frame.StaticCapacity)
                gpu_store<uint>(frame.StaticFreeList, pushPos, poolIdx);
            gpu_store<uint>(frame.StaticReverseTable, poolIdx, WORLD3D_VOXEL_INVALID_INDEX);
            gpu_store<uint>(frame.StaticPageTable, pageSlot, WORLD3D_VOXEL_BRICK_EMPTY);
            gpu_atomic_add_u32(gpu_offset_bytes(frame.Counters, VOXEL_COUNTER_STATIC_RECLAIMED * 4u), 1u);
        }
    }
}
