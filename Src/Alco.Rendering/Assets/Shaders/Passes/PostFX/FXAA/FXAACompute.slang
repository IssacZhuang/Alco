// file "FXAACompute.slang" - module "FXAACompute"
//
// Single-pass cooperative LDS compute FXAA pass (16x16 threadgroup caching an
// 18x19 bank-conflict-free luma/depth tile with 1-texel halo).

#language slang 2025
module FXAACompute;

import Core;
import AlcoRendering_FXAA;

[GPURoot]
public struct FxaaRoot
{
    public float2 InvSourceSize;
    public uint2 SourceSize;
    public GpuTextureId Source;
    public GpuTextureId Depth;
    public GpuTextureId Dest;
    public uint IsPassthrough;
    public float EdgeThreshold;
    public float EdgeThresholdMin;
    public float SubpixQuality;
    public float DepthThresholdMin;
    public float DepthThresholdRel;
    public float ColorVisibilityFloor;
};

groupshared float gsLuma[FXAA_CACHE_DIM][FXAA_CACHE_STRIDE];
groupshared float gsDepth[FXAA_CACHE_DIM][FXAA_CACHE_STRIDE];

[shader("compute")]
[numthreads(FXAA_TILE_DIM, FXAA_TILE_DIM, 1)]
void mainCS<let Quality : int, let Mode : int, let ColorSpace : int>(
    uint3 groupId : SV_GroupID,
    uint3 groupThread : SV_GroupThreadID)
{
    FxaaRoot root = gpu_load_root<FxaaRoot>();
    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);

    int2 pixel = int2(groupId.xy * FXAA_TILE_DIM + groupThread.xy);
    if (root.IsPassthrough != 0u)
    {
        if (pixel.x < int(root.SourceSize.x) && pixel.y < int(root.SourceSize.y))
        {
            RWTexture2D<float4> dest = gpu_texture<RWTexture2D<float4>>(root.Dest.index);
            dest[uint2(pixel)] = source.Load(int3(pixel, 0));
        }
        return;
    }

    int2 maxCoord = max(int2(root.SourceSize) - int2(1, 1), int2(0, 0));
    int2 clampedPixel = clamp(pixel, int2(0, 0), maxCoord);
    float4 centerColor = source.Load(int3(clampedPixel, 0));

    uint tx = groupThread.x;
    uint ty = groupThread.y;
    gsLuma[ty + 1][tx + 1] = fxaaComputeLuma<ColorSpace>(centerColor.rgb);

    if (Mode != FXAA_MODE_LUMA)
    {
        Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.Depth.index);
        gsDepth[ty + 1][tx + 1] = depthTex.Load(int3(clampedPixel, 0));
    }

    uint flatId = ty * FXAA_TILE_DIM + tx;
    if (flatId < FXAA_HALO_COUNT)
    {
        uint lx;
        uint ly;
        if (flatId < 18u)
        {
            lx = flatId;
            ly = 0u;
        }
        else if (flatId < 36u)
        {
            lx = flatId - 18u;
            ly = 17u;
        }
        else if (flatId < 52u)
        {
            lx = 0u;
            ly = 1u + (flatId - 36u);
        }
        else
        {
            lx = 17u;
            ly = 1u + (flatId - 52u);
        }

        int2 tileBase = int2(groupId.xy * FXAA_TILE_DIM) - int2(1, 1);
        int2 haloCoord = clamp(tileBase + int2(int(lx), int(ly)), int2(0, 0), maxCoord);
        gsLuma[ly][lx] = fxaaComputeLuma<ColorSpace>(source.Load(int3(haloCoord, 0)).rgb);

        if (Mode != FXAA_MODE_LUMA)
        {
            Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.Depth.index);
            gsDepth[ly][lx] = depthTex.Load(int3(haloCoord, 0));
        }
    }

    GroupMemoryBarrierWithGroupSync();

    if (pixel.x >= int(root.SourceSize.x) || pixel.y >= int(root.SourceSize.y))
    {
        return;
    }

    uint cx = tx + 1u;
    uint cy = ty + 1u;
    float lumaM = gsLuma[cy][cx];
    float lumaN = gsLuma[cy - 1u][cx];
    float lumaS = gsLuma[cy + 1u][cx];
    float lumaW = gsLuma[cy][cx - 1u];
    float lumaE = gsLuma[cy][cx + 1u];

    float rangeMin = min(lumaM, min(min(lumaN, lumaS), min(lumaW, lumaE)));
    float rangeMax = max(lumaM, max(max(lumaN, lumaS), max(lumaW, lumaE)));
    float range = rangeMax - rangeMin;

    bool depthEdge = false;
    float dN = 0.0f; float dS = 0.0f; float dW = 0.0f; float dM = 0.0f; float dE = 0.0f;

    if (Mode != FXAA_MODE_LUMA)
    {
        dM = gsDepth[cy][cx];
        dN = gsDepth[cy - 1u][cx];
        dS = gsDepth[cy + 1u][cx];
        dW = gsDepth[cy][cx - 1u];
        dE = gsDepth[cy][cx + 1u];
        depthEdge = fxaaDetectDepthCross(dM, dN, dS, dW, dE, root.DepthThresholdMin, root.DepthThresholdRel);
    }

    FxaaSettings settings;
    settings.InvSourceSize = root.InvSourceSize;
    settings.EdgeThreshold = root.EdgeThreshold;
    settings.EdgeThresholdMin = root.EdgeThresholdMin;
    settings.SubpixQuality = root.SubpixQuality;
    settings.ColorVisibilityFloor = root.ColorVisibilityFloor;

    RWTexture2D<float4> dest = gpu_texture<RWTexture2D<float4>>(root.Dest.index);
    if (!fxaaShouldProcessEdge<Mode>(range, rangeMax, depthEdge, settings))
    {
        dest[uint2(pixel)] = centerColor;
        return;
    }

    float lumaNW = gsLuma[cy - 1u][cx - 1u];
    float lumaNE = gsLuma[cy - 1u][cx + 1u];
    float lumaSW = gsLuma[cy + 1u][cx - 1u];
    float lumaSE = gsLuma[cy + 1u][cx + 1u];

    bool depthHorzSpan = false;
    bool depthPairN = false;
    if (Mode != FXAA_MODE_LUMA)
    {
        if (depthEdge)
        {
            float dNW = gsDepth[cy - 1u][cx - 1u];
            float dNE = gsDepth[cy - 1u][cx + 1u];
            float dSW = gsDepth[cy + 1u][cx - 1u];
            float dSE = gsDepth[cy + 1u][cx + 1u];
            fxaaAnalyzeDepthOrientation(dNW, dN, dNE, dW, dM, dE, dSW, dS, dSE, depthHorzSpan, depthPairN);
        }
    }

    float2 uv = (float2(pixel) + 0.5f) * root.InvSourceSize;
    dest[uint2(pixel)] = fxaaResolveEdge<Quality, Mode, ColorSpace>(
        settings,
        source,
        uv,
        centerColor,
        lumaM,
        lumaN, lumaS, lumaW, lumaE,
        lumaNW, lumaNE, lumaSW, lumaSE,
        range,
        depthEdge,
        depthHorzSpan,
        depthPairN);
}
