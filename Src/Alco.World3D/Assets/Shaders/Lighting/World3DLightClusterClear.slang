#language slang 2025
module World3DLightClusterClear;

import Core;
import World3DLightClusterCommon;

[GPURoot]
public struct LightClusterClearRoot
{
    public GPUBufferView Bins;
    public GPUBufferView Counters;
    public uint BinCount;
    public uint WordsPerBin;
};

[shader("compute")]
[numthreads(64, 1, 1)]
void MainCS(uint3 id : SV_DispatchThreadID)
{
    LightClusterClearRoot root = gpu_load_root<LightClusterClearRoot>();
    if (id.x < root.BinCount * root.WordsPerBin)
        gpu_store<uint>(root.Bins, id.x, 0u);
    if (id.x == 0u)
        gpu_store<World3DLightClusterCountersGpu>(root.Counters, 0u, (World3DLightClusterCountersGpu)0);
}
