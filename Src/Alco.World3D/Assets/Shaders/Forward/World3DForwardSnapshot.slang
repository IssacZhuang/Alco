#language slang 2025
module World3DForwardSnapshot;

import Core;

[GPURoot]
public struct ForwardSnapshotRoot
{
    public GPUTextureId SrcTex;
    public GPUTextureId DstTex;
    public uint Width;
    public uint Height;
    public uint Reserved0;
    public uint Reserved1;
};

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    ForwardSnapshotRoot root = gpu_load_root<ForwardSnapshotRoot>();
    if (id.x >= root.Width || id.y >= root.Height)
        return;

    Texture2D<float4> src = gpu_texture<Texture2D<float4>>(root.SrcTex.index);
    RWTexture2D<float4> dst = gpu_texture<RWTexture2D<float4>>(root.DstTex.index);
    dst[id.xy] = src.Load(int3(id.xy, 0));
}
