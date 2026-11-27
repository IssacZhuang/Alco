#language slang 2025
module World3DShadowMinMaxDilate;

import Core;

[GPURoot]
public struct ShadowMinMaxDilateRoot
{
    public GPUTextureId SrcMinMaxArray;
    public GPUTextureId DstMinMaxArray;
    public uint MinMaxResolution;
    public uint SliceIndex;
    public uint Reserved0;
    public uint Reserved1;
};

[shader("compute")]
[numthreads(8, 8, 1)]
public void MainCS(uint3 id : SV_DispatchThreadID)
{
    ShadowMinMaxDilateRoot root = gpu_load_root<ShadowMinMaxDilateRoot>();
    if (id.x >= root.MinMaxResolution || id.y >= root.MinMaxResolution)
        return;

    Texture2DArray<float2> src = gpu_texture<Texture2DArray<float2>>(root.SrcMinMaxArray.index);
    int maxCoord = int(root.MinMaxResolution) - 1;
    int slice = int(root.SliceIndex);

    float minD = 1.0;
    float maxD = 0.0;
    [unroll]
    for (int dy = -1; dy <= 1; ++dy)
    {
        int sy = clamp(int(id.y) + dy, 0, maxCoord);
        [unroll]
        for (int dx = -1; dx <= 1; ++dx)
        {
            int sx = clamp(int(id.x) + dx, 0, maxCoord);
            float2 d = src.Load(int4(sx, sy, slice, 0));
            minD = min(minD, d.x);
            maxD = max(maxD, d.y);
        }
    }

    RWTexture2DArray<float2> dst = gpu_texture<RWTexture2DArray<float2>>(root.DstMinMaxArray.index);
    dst[uint3(id.x, id.y, root.SliceIndex)] = float2(minD, maxD);
}
