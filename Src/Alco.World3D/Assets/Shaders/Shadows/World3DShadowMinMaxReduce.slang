#language slang 2025
module World3DShadowMinMaxReduce;

import Core;

[GPURoot]
public struct ShadowMinMaxReduceRoot
{
    public GPUTextureId ShadowArray;
    public GPUTextureId DstMinMaxArray;
    public uint CascadeResolution;
    public uint MinMaxResolution;
    public uint SliceIndex;
    public uint BlockSize;
};

[shader("compute")]
[numthreads(8, 8, 1)]
public void MainCS(uint3 id : SV_DispatchThreadID)
{
    ShadowMinMaxReduceRoot root = gpu_load_root<ShadowMinMaxReduceRoot>();
    if (id.x >= root.MinMaxResolution || id.y >= root.MinMaxResolution)
        return;

    Texture2DArray<float> src = gpu_texture<Texture2DArray<float>>(root.ShadowArray.index);
    uint blockSize = max(root.BlockSize, 1u);
    uint2 origin = id.xy * blockSize;
    uint maxCoord = root.CascadeResolution - 1u;

    float minD = 1.0;
    float maxD = 0.0;
    for (uint dy = 0u; dy < blockSize; ++dy)
    {
        uint sy = min(origin.y + dy, maxCoord);
        for (uint dx = 0u; dx < blockSize; ++dx)
        {
            uint sx = min(origin.x + dx, maxCoord);
            float d = src.Load(int4(int(sx), int(sy), int(root.SliceIndex), 0));
            minD = min(minD, d);
            maxD = max(maxD, d);
        }
    }

    RWTexture2DArray<float2> dst = gpu_texture<RWTexture2DArray<float2>>(root.DstMinMaxArray.index);
    dst[uint3(id.x, id.y, root.SliceIndex)] = float2(minD, maxD);
}
