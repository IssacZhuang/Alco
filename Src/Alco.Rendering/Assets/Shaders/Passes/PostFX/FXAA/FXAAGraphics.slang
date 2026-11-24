// file "FXAAGraphics.slang" - module "FXAAGraphics"
//
// Single-pass VB-less fullscreen triangle FXAA pass for swapchain and
// non-storage render targets, sharing the AlcoRendering_FXAA core algorithm.

#language slang 2025
module FXAAGraphics;

import Core;
import AlcoRendering_FXAA;

[GPURoot]
public struct FxaaRoot
{
    public float2 InvSourceSize;
    public uint2 SourceSize;
    public GPUTextureId Source;
    public GPUTextureId Depth;
    public GPUTextureId Dest;
    public uint IsPassthrough;
    public float EdgeThreshold;
    public float EdgeThresholdMin;
    public float SubpixQuality;
    public float DepthThresholdMin;
    public float DepthThresholdRel;
    public float ColorVisibilityFloor;
};

struct V2F
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(uint vertexIndex : SV_VertexID)
{
    float2 uv = float2((vertexIndex << 1) & 2, vertexIndex & 2);
    V2F output;
    output.Position = float4(uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    output.Uv = uv;
    return output;
}

[shader("pixel")]
float4 MainPS<let Quality : int, let Mode : int, let ColorSpace : int>(V2F input) : SV_Target0
{
    FxaaRoot root = gpu_load_root<FxaaRoot>();
    Texture2D<float4> source = gpu_texture<Texture2D<float4>>(root.Source.index);

    int2 maxCoord = max(int2(root.SourceSize) - int2(1, 1), int2(0, 0));
    int2 pixel = clamp(int2(input.Uv * float2(root.SourceSize)), int2(0, 0), maxCoord);
    float4 centerColor = source.Load(int3(pixel, 0));

    if (root.IsPassthrough != 0u)
    {
        return centerColor;
    }

    bool depthEdge = false;
    float dM = 0.0f; float dN = 0.0f; float dS = 0.0f; float dW = 0.0f; float dE = 0.0f;

    if (Mode != FXAA_MODE_LUMA)
    {
        Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.Depth.index);
        dM = depthTex.Load(int3(pixel, 0));
        dN = depthTex.Load(int3(clamp(pixel + int2(0, -1), int2(0, 0), maxCoord), 0));
        dS = depthTex.Load(int3(clamp(pixel + int2(0,  1), int2(0, 0), maxCoord), 0));
        dW = depthTex.Load(int3(clamp(pixel + int2(-1, 0), int2(0, 0), maxCoord), 0));
        dE = depthTex.Load(int3(clamp(pixel + int2( 1, 0), int2(0, 0), maxCoord), 0));
        depthEdge = fxaaDetectDepthCross(dM, dN, dS, dW, dE, root.DepthThresholdMin, root.DepthThresholdRel);

        // In DepthOnly mode, skip color neighbor fetches on flat geometry.
        if (Mode == FXAA_MODE_DEPTH_ONLY && !depthEdge)
        {
            return centerColor;
        }
    }

    float lumaM = fxaaComputeLuma<ColorSpace>(centerColor.rgb);
    float lumaN = fxaaComputeLuma<ColorSpace>(source.Load(int3(clamp(pixel + int2(0, -1), int2(0, 0), maxCoord), 0)).rgb);
    float lumaS = fxaaComputeLuma<ColorSpace>(source.Load(int3(clamp(pixel + int2(0,  1), int2(0, 0), maxCoord), 0)).rgb);
    float lumaW = fxaaComputeLuma<ColorSpace>(source.Load(int3(clamp(pixel + int2(-1, 0), int2(0, 0), maxCoord), 0)).rgb);
    float lumaE = fxaaComputeLuma<ColorSpace>(source.Load(int3(clamp(pixel + int2( 1, 0), int2(0, 0), maxCoord), 0)).rgb);

    float rangeMin = min(lumaM, min(min(lumaN, lumaS), min(lumaW, lumaE)));
    float rangeMax = max(lumaM, max(max(lumaN, lumaS), max(lumaW, lumaE)));
    float range = rangeMax - rangeMin;

    FxaaSettings settings;
    settings.InvSourceSize = root.InvSourceSize;
    settings.EdgeThreshold = root.EdgeThreshold;
    settings.EdgeThresholdMin = root.EdgeThresholdMin;
    settings.SubpixQuality = root.SubpixQuality;
    settings.ColorVisibilityFloor = root.ColorVisibilityFloor;

    if (!fxaaShouldProcessEdge<Mode>(range, rangeMax, depthEdge, settings))
    {
        return centerColor;
    }

    float lumaNW = fxaaComputeLuma<ColorSpace>(source.Load(int3(clamp(pixel + int2(-1, -1), int2(0, 0), maxCoord), 0)).rgb);
    float lumaNE = fxaaComputeLuma<ColorSpace>(source.Load(int3(clamp(pixel + int2( 1, -1), int2(0, 0), maxCoord), 0)).rgb);
    float lumaSW = fxaaComputeLuma<ColorSpace>(source.Load(int3(clamp(pixel + int2(-1,  1), int2(0, 0), maxCoord), 0)).rgb);
    float lumaSE = fxaaComputeLuma<ColorSpace>(source.Load(int3(clamp(pixel + int2( 1,  1), int2(0, 0), maxCoord), 0)).rgb);

    bool depthHorzSpan = false;
    bool depthPairN = false;
    if (Mode != FXAA_MODE_LUMA)
    {
        if (depthEdge)
        {
            Texture2D<float> depthTex = gpu_texture<Texture2D<float>>(root.Depth.index);
            float dNW = depthTex.Load(int3(clamp(pixel + int2(-1, -1), int2(0, 0), maxCoord), 0));
            float dNE = depthTex.Load(int3(clamp(pixel + int2( 1, -1), int2(0, 0), maxCoord), 0));
            float dSW = depthTex.Load(int3(clamp(pixel + int2(-1,  1), int2(0, 0), maxCoord), 0));
            float dSE = depthTex.Load(int3(clamp(pixel + int2( 1,  1), int2(0, 0), maxCoord), 0));
            fxaaAnalyzeDepthOrientation(dNW, dN, dNE, dW, dM, dE, dSW, dS, dSE, depthHorzSpan, depthPairN);
        }
    }

    float2 uv = (float2(pixel) + 0.5f) * root.InvSourceSize;
    return fxaaResolveEdge<Quality, Mode, ColorSpace>(
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
