#language slang 2025
module World3DVoxelMipChain;

import Core;

[GPURoot]
public struct VoxelMipChainRoot
{
    public GPUTextureId SrcTex;
    public GPUTextureId DstMip1;
    public GPUTextureId DstMip2;
    public GPUTextureId DstMip3;
    public uint SrcMip;
    public uint DstMip1Size;
    public uint DstMip2Size;
    public uint DstMip3Size;
};

groupshared float4 sMip1[4][4][4];
groupshared float4 sMip2[2][2][2];

[shader("compute")]
[numthreads(4, 4, 4)]
public void csMain(
    uint3 id : SV_DispatchThreadID,
    uint3 groupId : SV_GroupID,
    uint3 localId : SV_GroupThreadID)
{
    VoxelMipChainRoot root = gpu_load_root<VoxelMipChainRoot>();
    float4 avg1 = float4(0.0);
    if (id.x < root.DstMip1Size && id.y < root.DstMip1Size && id.z < root.DstMip1Size)
    {
        Texture3D<float4> src = gpu_texture<Texture3D<float4>>(root.SrcTex.index);
        RWTexture3D<float4> dst1 = gpu_texture<RWTexture3D<float4>>(root.DstMip1.index);
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
        avg1 = sum * 0.125;
        dst1[id] = avg1;
    }
    sMip1[localId.x][localId.y][localId.z] = avg1;
    GroupMemoryBarrierWithGroupSync();

    if ((localId.x & 1u) == 0u && (localId.y & 1u) == 0u && (localId.z & 1u) == 0u && root.DstMip2Size > 0u)
    {
        float4 sum2 = sMip1[localId.x + 0u][localId.y + 0u][localId.z + 0u]
            + sMip1[localId.x + 1u][localId.y + 0u][localId.z + 0u]
            + sMip1[localId.x + 0u][localId.y + 1u][localId.z + 0u]
            + sMip1[localId.x + 1u][localId.y + 1u][localId.z + 0u]
            + sMip1[localId.x + 0u][localId.y + 0u][localId.z + 1u]
            + sMip1[localId.x + 1u][localId.y + 0u][localId.z + 1u]
            + sMip1[localId.x + 0u][localId.y + 1u][localId.z + 1u]
            + sMip1[localId.x + 1u][localId.y + 1u][localId.z + 1u];
        float4 avg2 = sum2 * 0.125;
        uint3 dst2Coord = id >> 1;
        if (dst2Coord.x < root.DstMip2Size && dst2Coord.y < root.DstMip2Size && dst2Coord.z < root.DstMip2Size)
            gpu_texture<RWTexture3D<float4>>(root.DstMip2.index)[dst2Coord] = avg2;
        sMip2[localId.x >> 1][localId.y >> 1][localId.z >> 1] = avg2;
    }
    GroupMemoryBarrierWithGroupSync();

    if (localId.x == 0u && localId.y == 0u && localId.z == 0u && root.DstMip3Size > 0u)
    {
        float4 sum3 = sMip2[0][0][0] + sMip2[1][0][0] + sMip2[0][1][0] + sMip2[1][1][0]
            + sMip2[0][0][1] + sMip2[1][0][1] + sMip2[0][1][1] + sMip2[1][1][1];
        float4 avg3 = sum3 * 0.125;
        if (groupId.x < root.DstMip3Size && groupId.y < root.DstMip3Size && groupId.z < root.DstMip3Size)
            gpu_texture<RWTexture3D<float4>>(root.DstMip3.index)[groupId] = avg3;
    }
}
