#language slang 2025
module World3DVoxelMip;

import Core;

[GPURoot]
public struct VoxelMipRoot
{
    public GPUTextureId SrcTex;
    public GPUTextureId DstTex;
    public uint SrcMip;
    public uint DstSize;
    public uint Reserved0;
    public uint Reserved1;
};

[shader("compute")]
[numthreads(4, 4, 4)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VoxelMipRoot root = gpu_load_root<VoxelMipRoot>();
    if (id.x >= root.DstSize || id.y >= root.DstSize || id.z >= root.DstSize)
        return;

    Texture3D<float4> src = gpu_texture<Texture3D<float4>>(root.SrcTex.index);
    RWTexture3D<float4> dst = gpu_texture<RWTexture3D<float4>>(root.DstTex.index);

    int3 baseCoord = int3(id) * 2;
    int mip = int(root.SrcMip);

    float4 sum = float4(0.0);
    sum += src.Load(int4(baseCoord + int3(0, 0, 0), mip));
    sum += src.Load(int4(baseCoord + int3(1, 0, 0), mip));
    sum += src.Load(int4(baseCoord + int3(0, 1, 0), mip));
    sum += src.Load(int4(baseCoord + int3(1, 1, 0), mip));
    sum += src.Load(int4(baseCoord + int3(0, 0, 1), mip));
    sum += src.Load(int4(baseCoord + int3(1, 0, 1), mip));
    sum += src.Load(int4(baseCoord + int3(0, 1, 1), mip));
    sum += src.Load(int4(baseCoord + int3(1, 1, 1), mip));

    dst[id] = sum * 0.125;
}
