#language slang 2025
module World3DHiZBuild;

import Core;

[GPURoot]
public struct HiZBuildRoot
{
    public GPUTextureId SrcTexture;
    public GPUTextureId DstTexture;
    public uint SrcWidth;
    public uint SrcHeight;
    public uint DstWidth;
    public uint DstHeight;
    public uint MipLevel;
    public uint Reserved0;
};

void accumulateMinMax(Texture2D<float> srcDepth, uint2 coord, uint2 maxCoord, inout float minVal, inout float maxVal)
{
    uint2 c = min(coord, maxCoord);
    float d = srcDepth.Load(int3(int(c.x), int(c.y), 0));
    minVal = min(minVal, d);
    maxVal = max(maxVal, d);
}

void accumulateMinMaxHiZ(Texture2D<float2> srcHiZ, int srcMip, uint2 coord, uint2 maxCoord, inout float minVal, inout float maxVal)
{
    uint2 c = min(coord, maxCoord);
    float2 d = srcHiZ.Load(int3(int(c.x), int(c.y), srcMip));
    minVal = min(minVal, d.x);
    maxVal = max(maxVal, d.y);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void MainCS(uint3 id : SV_DispatchThreadID)
{
    HiZBuildRoot root = gpu_load_root<HiZBuildRoot>();
    if (id.x >= root.DstWidth || id.y >= root.DstHeight)
        return;

    uint2 baseCoord = id.xy * 2u;
    uint2 srcMax = uint2(root.SrcWidth - 1u, root.SrcHeight - 1u);
    bool tailX = (id.x == root.DstWidth - 1u) && (root.SrcWidth > root.DstWidth * 2u);
    bool tailY = (id.y == root.DstHeight - 1u) && (root.SrcHeight > root.DstHeight * 2u);

    float minVal = 1.0;
    float maxVal = 0.0;

    if (root.MipLevel == 0u)
    {
        Texture2D<float> src = gpu_texture<Texture2D<float>>(root.SrcTexture.index);
        accumulateMinMax(src, baseCoord + uint2(0u, 0u), srcMax, minVal, maxVal);
        accumulateMinMax(src, baseCoord + uint2(1u, 0u), srcMax, minVal, maxVal);
        accumulateMinMax(src, baseCoord + uint2(0u, 1u), srcMax, minVal, maxVal);
        accumulateMinMax(src, baseCoord + uint2(1u, 1u), srcMax, minVal, maxVal);
        if (tailX)
        {
            accumulateMinMax(src, baseCoord + uint2(2u, 0u), srcMax, minVal, maxVal);
            accumulateMinMax(src, baseCoord + uint2(2u, 1u), srcMax, minVal, maxVal);
        }
        if (tailY)
        {
            accumulateMinMax(src, baseCoord + uint2(0u, 2u), srcMax, minVal, maxVal);
            accumulateMinMax(src, baseCoord + uint2(1u, 2u), srcMax, minVal, maxVal);
        }
        if (tailX && tailY)
            accumulateMinMax(src, baseCoord + uint2(2u, 2u), srcMax, minVal, maxVal);
    }
    else
    {
        Texture2D<float2> src = gpu_texture<Texture2D<float2>>(root.SrcTexture.index);
        int srcMip = int(root.MipLevel - 1u);
        accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(0u, 0u), srcMax, minVal, maxVal);
        accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(1u, 0u), srcMax, minVal, maxVal);
        accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(0u, 1u), srcMax, minVal, maxVal);
        accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(1u, 1u), srcMax, minVal, maxVal);
        if (tailX)
        {
            accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(2u, 0u), srcMax, minVal, maxVal);
            accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(2u, 1u), srcMax, minVal, maxVal);
        }
        if (tailY)
        {
            accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(0u, 2u), srcMax, minVal, maxVal);
            accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(1u, 2u), srcMax, minVal, maxVal);
        }
        if (tailX && tailY)
            accumulateMinMaxHiZ(src, srcMip, baseCoord + uint2(2u, 2u), srcMax, minVal, maxVal);
    }

    RWTexture2D<float2> dst = gpu_texture<RWTexture2D<float2>>(root.DstTexture.index);
    dst[id.xy] = float2(minVal, maxVal);
}
