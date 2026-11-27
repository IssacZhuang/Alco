#language slang 2025
module World3DOcclusionPrepare;

import Core;

[GPURoot]
public struct OcclusionPrepareRoot
{
    public GPUBufferView Counters;
    public GPUBufferView RetestDispatch;
    public GPUBufferView Phase2BuildDispatch;
    public GPUBufferView HiZRefreshDispatches;
    public uint Mode;
    public uint CandGroupsX;
    public uint CandGroupsY;
    public uint HiZMip0Width;
    public uint HiZMip0Height;
    public uint HiZMipCount;
    public uint MaxInstances;
    public uint Reserved0;
};

[shader("compute")]
[numthreads(16, 1, 1)]
public void MainCS(uint3 id : SV_DispatchThreadID)
{
    OcclusionPrepareRoot root = gpu_load_root<OcclusionPrepareRoot>();
    if (id.x >= 16u)
        return;

    if (root.Mode == 0u)
    {
        if (id.x < 4u)
            gpu_store<uint>(root.Counters, id.x, 0u);
        if (id.x == 0u)
        {
            gpu_store<uint4>(root.RetestDispatch, 0u, uint4(0u, 0u, 0u, 0u));
            gpu_store<uint4>(root.Phase2BuildDispatch, 0u, uint4(0u, 0u, 0u, 0u));
        }
        gpu_store<uint4>(root.HiZRefreshDispatches, id.x, uint4(0u, 0u, 0u, 0u));
        return;
    }

    if (root.Mode == 1u)
    {
        if (id.x != 0u)
            return;
        uint occluded = min(gpu_load<uint>(root.Counters, 0u), root.MaxInstances);
        gpu_store<uint>(root.Counters, 0u, occluded);
        uint4 args = occluded == 0u
            ? uint4(0u, 0u, 0u, 0u)
            : uint4((occluded + 63u) / 64u, 1u, 1u, 0u);
        gpu_store<uint4>(root.RetestDispatch, 0u, args);
        return;
    }

    uint resurrected = gpu_load<uint>(root.Counters, 2u);
    if (id.x == 0u)
    {
        uint4 buildArgs = (resurrected == 0u || root.CandGroupsX == 0u || root.CandGroupsY == 0u)
            ? uint4(0u, 0u, 0u, 0u)
            : uint4(root.CandGroupsX, root.CandGroupsY, 1u, 0u);
        gpu_store<uint4>(root.Phase2BuildDispatch, 0u, buildArgs);
    }

    uint4 hizArgs = uint4(0u, 0u, 0u, 0u);
    if (resurrected > 0u && id.x < root.HiZMipCount)
    {
        uint w = max(1u, root.HiZMip0Width >> id.x);
        uint h = max(1u, root.HiZMip0Height >> id.x);
        hizArgs = uint4((w + 7u) / 8u, (h + 7u) / 8u, 1u, 0u);
    }
    gpu_store<uint4>(root.HiZRefreshDispatches, id.x, hizArgs);
}
