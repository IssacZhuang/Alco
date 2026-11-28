#language slang 2025
module World3DFarHeightBuild;

import Core;

public static const float WORLD3D_FAR_HEIGHT_VOID = -1e6;

[GPURoot]
public struct FarHeightBuildRoot
{
    public GPUTextureId CaptureDepthTex;
    public GPUTextureId ExplicitBaseTex;
    public GPUTextureId SrcMipTex;
    public GPUTextureId DstMipTex;
    public uint SrcWidth;
    public uint SrcHeight;
    public uint DstWidth;
    public uint DstHeight;
    public uint MipLevel;
    public uint Flags; // bit 0 = HasCaptureDepth, bit 1 = HasExplicitBase
    public float CaptureZMax;
    public float CaptureZSpan;
};

void foldMinMax(float2 sampleVal, inout float maxH, inout float minH)
{
    if (sampleVal.x > -99999.0)
        maxH = max(maxH, sampleVal.x);
    if (sampleVal.y > -99999.0 && sampleVal.y < 1e5)
        minH = min(minH, sampleVal.y);
}

void accumulateHeightMip(Texture2D<float2> srcTex, int srcMip, uint2 coord, uint2 maxCoord, inout float maxH, inout float minH)
{
    uint2 c = min(coord, maxCoord);
    float2 v = srcTex.Load(int3(int(c.x), int(c.y), srcMip));
    foldMinMax(v, maxH, minH);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void MainCS(uint3 id : SV_DispatchThreadID)
{
    FarHeightBuildRoot root = gpu_load_root<FarHeightBuildRoot>();
    if (id.x >= root.DstWidth || id.y >= root.DstHeight)
        return;

    RWTexture2D<float2> dst = gpu_texture<RWTexture2D<float2>>(root.DstMipTex.index);
    if (root.MipLevel == 0u)
    {
        float maxH = WORLD3D_FAR_HEIGHT_VOID;
        float minH = 1e6;
        bool hasAny = false;

        if ((root.Flags & 1u) != 0u && root.CaptureDepthTex.index != 0u)
        {
            Texture2D<float> captureTex = gpu_texture<Texture2D<float>>(root.CaptureDepthTex.index);
            float d = captureTex.Load(int3(int(id.x), int(id.y), 0));
            if (d < 1.0 - 1e-6)
            {
                float h = root.CaptureZMax - d * root.CaptureZSpan;
                maxH = max(maxH, h);
                minH = min(minH, h);
                hasAny = true;
            }
        }

        if ((root.Flags & 2u) != 0u && root.ExplicitBaseTex.index != 0u)
        {
            Texture2D<float> baseTex = gpu_texture<Texture2D<float>>(root.ExplicitBaseTex.index);
            float h = baseTex.Load(int3(int(id.x), int(id.y), 0));
            if (h > -99999.0)
            {
                maxH = max(maxH, h);
                minH = min(minH, h);
                hasAny = true;
            }
        }

        if (!hasAny)
            minH = WORLD3D_FAR_HEIGHT_VOID;
        dst[id.xy] = float2(maxH, minH);
        return;
    }

    Texture2D<float2> src = gpu_texture<Texture2D<float2>>(root.SrcMipTex.index);
    int srcMip = int(root.MipLevel - 1u);
    uint2 baseCoord = id.xy * 2u;
    uint2 srcMax = uint2(root.SrcWidth - 1u, root.SrcHeight - 1u);
    bool tailX = (id.x == root.DstWidth - 1u) && (root.SrcWidth > root.DstWidth * 2u);
    bool tailY = (id.y == root.DstHeight - 1u) && (root.SrcHeight > root.DstHeight * 2u);

    float maxH = WORLD3D_FAR_HEIGHT_VOID;
    float minH = 1e6;

    accumulateHeightMip(src, srcMip, baseCoord + uint2(0u, 0u), srcMax, maxH, minH);
    accumulateHeightMip(src, srcMip, baseCoord + uint2(1u, 0u), srcMax, maxH, minH);
    accumulateHeightMip(src, srcMip, baseCoord + uint2(0u, 1u), srcMax, maxH, minH);
    accumulateHeightMip(src, srcMip, baseCoord + uint2(1u, 1u), srcMax, maxH, minH);
    if (tailX)
    {
        accumulateHeightMip(src, srcMip, baseCoord + uint2(2u, 0u), srcMax, maxH, minH);
        accumulateHeightMip(src, srcMip, baseCoord + uint2(2u, 1u), srcMax, maxH, minH);
    }
    if (tailY)
    {
        accumulateHeightMip(src, srcMip, baseCoord + uint2(0u, 2u), srcMax, maxH, minH);
        accumulateHeightMip(src, srcMip, baseCoord + uint2(1u, 2u), srcMax, maxH, minH);
    }
    if (tailX && tailY)
        accumulateHeightMip(src, srcMip, baseCoord + uint2(2u, 2u), srcMax, maxH, minH);

    if (minH > 1e5)
        minH = WORLD3D_FAR_HEIGHT_VOID;
    dst[id.xy] = float2(maxH, minH);
}
