#language slang 2025
module SpriteSceneSortLocal;

import Core;
import AlcoRendering_SpriteScene;

public static const uint SORT_LOCAL_THREADS = 256u;
public static const uint SORT_LOCAL_BLOCK = 1024u;
public static const uint SORT_LOCAL_MODE_SORT = 0u;
public static const uint SORT_LOCAL_MODE_DISPERSE = 1u;

[GPURoot]
public struct SpriteSceneSortLocalRoot
{
    public GPUBufferView Pool;
    public GPUBufferView DrawArgs;
    public GPUBufferView VisibleSlots;
    public GPUBufferView VisibleKeys;
    public uint BucketId;
    public uint VisibleBase;
    public uint Mode;
    public uint Pad0;
};

groupshared uint s_keys[SORT_LOCAL_BLOCK];
groupshared uint s_slots[SORT_LOCAL_BLOCK];

uint floatToSortableUintDesc(float f)
{
    uint u = asuint(f);
    if (u == 0x80000000u)
    {
        u = 0u;
    }
    uint mask = (u & 0x80000000u) != 0u ? 0xFFFFFFFFu : 0x80000000u;
    return ~(u ^ mask);
}

bool sortShouldSwap(uint keyA, uint slotA, uint keyB, uint slotB)
{
    return (keyA > keyB) || (keyA == keyB && slotA > slotB);
}

void localCompareSwap(uint i, uint j, uint blockCount)
{
    if (j < blockCount)
    {
        uint keyA = s_keys[i];
        uint slotA = s_slots[i];
        uint keyB = s_keys[j];
        uint slotB = s_slots[j];
        if (sortShouldSwap(keyA, slotA, keyB, slotB))
        {
            s_keys[i] = keyB;
            s_slots[i] = slotB;
            s_keys[j] = keyA;
            s_slots[j] = slotA;
        }
    }
}

[shader("compute")]
[numthreads(256, 1, 1)]
public void mainCS(uint3 groupId : SV_GroupID, uint3 groupThreadId : SV_GroupThreadID)
{
    SpriteSceneSortLocalRoot root = gpu_load_root<SpriteSceneSortLocalRoot>();
    uint count = gpu_load<uint>(gpu_offset_bytes(root.DrawArgs, root.BucketId * 16u + 4u));
    uint blockOffset = groupId.x * SORT_LOCAL_BLOCK;
    if (blockOffset >= count)
    {
        return;
    }

    uint blockCount = min(SORT_LOCAL_BLOCK, count - blockOffset);
    uint globalBase = root.VisibleBase + blockOffset;
    uint tid = groupThreadId.x;

    for (uint e = 0u; e < 4u; ++e)
    {
        uint idx = tid + e * SORT_LOCAL_THREADS;
        if (idx < blockCount)
        {
            uint slot = gpu_load<uint>(root.VisibleSlots, globalBase + idx);
            s_slots[idx] = slot;
            if (root.Mode == SORT_LOCAL_MODE_SORT)
            {
                SpriteSceneInstance inst = gpu_load<SpriteSceneInstance>(root.Pool, slot);
                s_keys[idx] = floatToSortableUintDesc(inst.Depth);
            }
            else
            {
                s_keys[idx] = gpu_load<uint>(root.VisibleKeys, globalBase + idx);
            }
        }
        else
        {
            s_slots[idx] = 0xFFFFFFFFu;
            s_keys[idx] = 0xFFFFFFFFu;
        }
    }
    GroupMemoryBarrierWithGroupSync();

    if (root.Mode == SORT_LOCAL_MODE_SORT)
    {
        for (uint stage = 0u; stage < 10u; ++stage)
        {
            uint halfH = 1u << stage;
            uint h = halfH << 1u;
            for (uint p = 0u; p < 2u; ++p)
            {
                uint pairIdx = tid + p * SORT_LOCAL_THREADS;
                uint q = pairIdx >> stage;
                uint r = pairIdx & (halfH - 1u);
                uint i = (q << (stage + 1u)) + r;
                uint j = (q << (stage + 1u)) + (h - 1u - r);
                localCompareSwap(i, j, blockCount);
            }
            GroupMemoryBarrierWithGroupSync();

            for (int sub = int(stage) - 1; sub >= 0; --sub)
            {
                uint subU = uint(sub);
                uint halfHH = 1u << subU;
                for (uint p = 0u; p < 2u; ++p)
                {
                    uint pairIdx = tid + p * SORT_LOCAL_THREADS;
                    uint q = pairIdx >> subU;
                    uint r = pairIdx & (halfHH - 1u);
                    uint i = (q << (subU + 1u)) + r;
                    uint j = i + halfHH;
                    localCompareSwap(i, j, blockCount);
                }
                GroupMemoryBarrierWithGroupSync();
            }
        }
    }
    else
    {
        for (int sub = 9; sub >= 0; --sub)
        {
            uint subU = uint(sub);
            uint halfHH = 1u << subU;
            for (uint p = 0u; p < 2u; ++p)
            {
                uint pairIdx = tid + p * SORT_LOCAL_THREADS;
                uint q = pairIdx >> subU;
                uint r = pairIdx & (halfHH - 1u);
                uint i = (q << (subU + 1u)) + r;
                uint j = i + halfHH;
                localCompareSwap(i, j, blockCount);
            }
            GroupMemoryBarrierWithGroupSync();
        }
    }

    for (uint e = 0u; e < 4u; ++e)
    {
        uint idx = tid + e * SORT_LOCAL_THREADS;
        if (idx < blockCount)
        {
            gpu_store<uint>(root.VisibleSlots, globalBase + idx, s_slots[idx]);
            gpu_store<uint>(root.VisibleKeys, globalBase + idx, s_keys[idx]);
        }
    }
}
