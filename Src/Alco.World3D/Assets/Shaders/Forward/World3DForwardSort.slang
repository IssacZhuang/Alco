#language slang 2025
module World3DForwardSort;

import Core;

[GPURoot]
public struct ForwardSortRoot
{
    public GPUBufferView SortKeys;
    public GPUBufferView Counters;
    public uint PaddedCount;
    public uint Mode;      // 0 = LocalInit (256), 1 = GlobalStep(k, j), 2 = LocalMerge(k), 3 = ClearCounters
    public uint StageK;
    public uint SubStepJ;
};

groupshared uint2 sTile[256];

[ForceInline]
bool shouldSwapKeys(uint2 a, uint2 b, bool ascending)
{
    bool aGreater = (a.x > b.x) || (a.x == b.x && a.y > b.y);
    bool aLess = (a.x < b.x) || (a.x == b.x && a.y < b.y);
    return ascending ? aGreater : aLess;
}

[shader("compute")]
[numthreads(256, 1, 1)]
public void csMain(uint3 id : SV_DispatchThreadID, uint3 groupId : SV_GroupID, uint lane : SV_GroupIndex)
{
    ForwardSortRoot root = gpu_load_root<ForwardSortRoot>();
    if (root.Mode == 3u)
    {
        if (id.x == 0u)
            gpu_store<uint4>(root.Counters, 0u, uint4(0u, 0u, 0u, 0u));
        return;
    }

    if (root.Mode == 1u)
    {
        uint idx = id.x;
        if (idx >= root.PaddedCount)
            return;

        uint partner = idx ^ root.SubStepJ;
        if (partner > idx && partner < root.PaddedCount)
        {
            bool asc = (idx & root.StageK) == 0u;
            uint2 a = gpu_load<uint2>(root.SortKeys, idx);
            uint2 b = gpu_load<uint2>(root.SortKeys, partner);
            if (shouldSwapKeys(a, b, asc))
            {
                gpu_store<uint2>(root.SortKeys, idx, b);
                gpu_store<uint2>(root.SortKeys, partner, a);
            }
        }
        return;
    }

    uint globalIdx = groupId.x * 256u + lane;
    sTile[lane] = globalIdx < root.PaddedCount
        ? gpu_load<uint2>(root.SortKeys, globalIdx)
        : uint2(0xFFFFFFFFu, 0xFFFFFFFFu);
    GroupMemoryBarrierWithGroupSync();

    if (root.Mode == 0u)
    {
        bool finalAsc = root.PaddedCount <= 256u || (groupId.x & 1u) == 0u;
        for (uint k = 2u; k <= 256u; k <<= 1u)
        {
            bool dir = (k == 256u) ? finalAsc : ((lane & k) == 0u);
            for (uint j = k >> 1u; j > 0u; j >>= 1u)
            {
                uint partner = lane ^ j;
                if (partner > lane)
                {
                    uint2 a = sTile[lane];
                    uint2 b = sTile[partner];
                    if (shouldSwapKeys(a, b, dir))
                    {
                        sTile[lane] = b;
                        sTile[partner] = a;
                    }
                }
                GroupMemoryBarrierWithGroupSync();
            }
        }
    }
    else
    {
        bool asc = (globalIdx & root.StageK) == 0u;
        for (uint j = 128u; j > 0u; j >>= 1u)
        {
            uint partner = lane ^ j;
            if (partner > lane)
            {
                uint2 a = sTile[lane];
                uint2 b = sTile[partner];
                if (shouldSwapKeys(a, b, asc))
                {
                    sTile[lane] = b;
                    sTile[partner] = a;
                }
            }
            GroupMemoryBarrierWithGroupSync();
        }
    }

    if (globalIdx < root.PaddedCount)
        gpu_store<uint2>(root.SortKeys, globalIdx, sTile[lane]);
}
