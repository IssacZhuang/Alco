#language slang 2025
module World3DVoxelUpsample;

import Core;

[GPURoot]
public struct VoxelUpsampleRoot
{
    public GPUTextureId DepthTex;
    public GPUTextureId NormalTex;
    public GPUTextureId HalfDiffuseTex;
    public GPUTextureId HalfSpecularTex;
    public GPUTextureId OutDiffuseTex;
    public GPUTextureId OutSpecularTex;
    public uint FullWidth;
    public uint FullHeight;
    public uint HalfWidth;
    public uint HalfHeight;
    public uint FrameIndex;
    public uint Reserved0;
};

uint2 halfCoordToFullPixel(uint2 halfCoord, uint frameIndex, uint2 fullSize)
{
    return min(halfCoord * 2u + uint2((halfCoord.x ^ frameIndex) & 1u, (halfCoord.y ^ (frameIndex >> 1)) & 1u), fullSize - 1u);
}

[shader("compute")]
[numthreads(8, 8, 1)]
public void csMain(uint3 id : SV_DispatchThreadID)
{
    VoxelUpsampleRoot root = gpu_load_root<VoxelUpsampleRoot>();
    uint2 fullSize = uint2(root.FullWidth, root.FullHeight);
    uint2 halfSize = uint2(root.HalfWidth, root.HalfHeight);
    if (any(id.xy >= fullSize))
        return;

    int3 fullInt = int3(int2(id.xy), 0);
    Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.DepthTex.index);
    Texture2D<float4> normalTex = gpu_texture<Texture2D<float4>>(root.NormalTex.index);
    Texture2D<float4> halfDiff = gpu_texture<Texture2D<float4>>(root.HalfDiffuseTex.index);
    Texture2D<float4> halfSpec = gpu_texture<Texture2D<float4>>(root.HalfSpecularTex.index);
    RWTexture2D<float4> outDiff = gpu_texture<RWTexture2D<float4>>(root.OutDiffuseTex.index);
    RWTexture2D<float4> outSpec = gpu_texture<RWTexture2D<float4>>(root.OutSpecularTex.index);

    float depth = depthTex.Load(fullInt);
    if (depth == 0.0)
    {
        outDiff[id.xy] = float4(0.0, 0.0, 0.0, 1.0);
        outSpec[id.xy] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 normal = normalize(normalTex.Load(fullInt).xyz * 2.0 - 1.0);

    float2 halfContinuous = (float2(id.xy) + 0.5) * 0.5 - 0.5;
    int2 baseH = int2(floor(halfContinuous));
    float2 fracH = saturate(halfContinuous - float2(baseH));
    int2 maxH = int2(halfSize) - 1;

    float4 accumDiff = float4(0.0);
    float4 accumSpec = float4(0.0);
    float weightSum = 0.0;

    [unroll]
    for (int oy = 0; oy < 2; ++oy)
    {
        [unroll]
        for (int ox = 0; ox < 2; ++ox)
        {
            uint2 hCoord = uint2(clamp(baseH + int2(ox, oy), int2(0), maxH));
            uint2 hFull = halfCoordToFullPixel(hCoord, root.FrameIndex, fullSize);
            int3 hFullInt = int3(int2(hFull), 0);

            float hDepth = depthTex.Load(hFullInt);
            float4 tapD = halfDiff.Load(int3(int2(hCoord), 0));
            float4 tapS = halfSpec.Load(int3(int2(hCoord), 0));
            if (hDepth == 0.0)
                continue;

            float3 hNormal = normalize(normalTex.Load(hFullInt).xyz * 2.0 - 1.0);
            float relDepth = abs(depth - hDepth) / max(depth, 1e-4);
            float wDepth = exp(-relDepth * 45.0);
            float wNorm = pow(saturate(dot(normal, hNormal)), 10.0);
            float wx = ox == 0 ? (1.0 - fracH.x) : fracH.x;
            float wy = oy == 0 ? (1.0 - fracH.y) : fracH.y;
            float w = max(wx * wy, 0.05) * wDepth * wNorm;

            accumDiff += tapD * w;
            accumSpec += tapS * w;
            weightSum += w;
        }
    }

    if (weightSum > 1e-5)
    {
        outDiff[id.xy] = accumDiff / weightSum;
        outSpec[id.xy] = accumSpec / weightSum;
    }
    else
    {
        uint2 fallbackCoord = uint2(clamp(baseH, int2(0), maxH));
        outDiff[id.xy] = halfDiff.Load(int3(int2(fallbackCoord), 0));
        outSpec[id.xy] = halfSpec.Load(int3(int2(fallbackCoord), 0));
    }
}
