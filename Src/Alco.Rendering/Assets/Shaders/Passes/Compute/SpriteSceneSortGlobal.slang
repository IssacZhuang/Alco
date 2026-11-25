#language slang 2025
module SpriteSceneSortGlobal;

import Core;

public static const uint SORT_GLOBAL_MODE_FLIP = 0u;
public static const uint SORT_GLOBAL_MODE_DISPERSE = 1u;

[GPURoot]
public struct SpriteSceneSortGlobalRoot
{
    public GPUBufferView DrawArgs;
    public GPUBufferView VisibleSlots;
    public GPUBufferView VisibleKeys;
    public uint BucketId;
    public uint VisibleBase;
    public uint HalfSpanShift;
    public uint Mode;
};

[shader("compute")]
[numthreads(256, 1, 1)]
public void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    SpriteSceneSortGlobalRoot root = gpu_load_root<SpriteSceneSortGlobalRoot>();
    uint count = gpu_load<uint>(gpu_offset_bytes(root.DrawArgs, root.BucketId * 16u + 4u));
    uint halfSpan = 1u << root.HalfSpanShift;
    if (count <= halfSpan)
    {
        return;
    }

    uint tid = dispatchId.x;
    uint q = tid >> root.HalfSpanShift;
    uint r = tid & (halfSpan - 1u);
    uint blockBase = q << (root.HalfSpanShift + 1u);
    uint i = blockBase + r;
    uint j = root.Mode == SORT_GLOBAL_MODE_FLIP
        ? (blockBase + ((halfSpan << 1u) - 1u - r))
        : (i + halfSpan);
    if (j >= count)
    {
        return;
    }

    uint idxA = root.VisibleBase + i;
    uint idxB = root.VisibleBase + j;
    uint keyA = gpu_load<uint>(root.VisibleKeys, idxA);
    uint slotA = gpu_load<uint>(root.VisibleSlots, idxA);
    uint keyB = gpu_load<uint>(root.VisibleKeys, idxB);
    uint slotB = gpu_load<uint>(root.VisibleSlots, idxB);
    if ((keyA > keyB) || (keyA == keyB && slotA > slotB))
    {
        gpu_store<uint>(root.VisibleKeys, idxA, keyB);
        gpu_store<uint>(root.VisibleSlots, idxA, slotB);
        gpu_store<uint>(root.VisibleKeys, idxB, keyA);
        gpu_store<uint>(root.VisibleSlots, idxB, slotA);
    }
}
