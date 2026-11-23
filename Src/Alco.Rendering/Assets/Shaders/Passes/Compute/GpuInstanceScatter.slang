#language slang 2025
module GpuInstanceScatter;

import Core;

[GPURoot]
public struct InstanceScatterRoot
{
    public GPUBufferView Patches;
    public GPUBufferView PatchSlots;
    public GPUBufferView Pool;
    public uint PatchCount;
    public uint WordsPerInstance;
};

[shader("compute")]
[numthreads(64, 1, 1)]
public void mainCS(uint3 id : SV_DispatchThreadID)
{
    InstanceScatterRoot root = gpu_load_root<InstanceScatterRoot>();
    uint patchIdx = id.x;
    if (patchIdx >= root.PatchCount)
    {
        return;
    }

    uint dstSlot = gpu_load<uint>(root.PatchSlots, patchIdx);
    uint srcBase = patchIdx * root.WordsPerInstance;
    uint dstBase = dstSlot * root.WordsPerInstance;
    for (uint w = 0u; w < root.WordsPerInstance; ++w)
    {
        uint word = gpu_load<uint>(root.Patches, srcBase + w);
        gpu_store<uint>(root.Pool, dstBase + w, word);
    }
}
