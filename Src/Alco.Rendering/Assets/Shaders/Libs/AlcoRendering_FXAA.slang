// file "AlcoRendering_FXAA.slang" - module "AlcoRendering_FXAA"
//
// Shared FXAA constants, settings payload, perceptual luma, second-order planar
// depth discontinuity, adaptive edge-walk tables, and directional subpixel filter.

#language slang 2025
module AlcoRendering_FXAA;

import Core;
import SharedSampler;

public static const uint FXAA_TILE_DIM = 16u;
public static const uint FXAA_CACHE_DIM = 18u;
public static const uint FXAA_CACHE_STRIDE = 19u; // +1 stride prevents 16/32-bank LDS conflicts across columns.
public static const uint FXAA_HALO_COUNT = 68u;   // 18 * 18 - 16 * 16 perimeter texels.

public static const int FXAA_QUALITY_LOW = 0;
public static const int FXAA_QUALITY_MEDIUM = 1;
public static const int FXAA_QUALITY_HIGH = 2;
public static const int FXAA_QUALITY_ULTRA = 3;

public static const int FXAA_MODE_LUMA = 0;
public static const int FXAA_MODE_DEPTH_ASSISTED = 1;
public static const int FXAA_MODE_DEPTH_ONLY = 2;

public static const int FXAA_COLOR_GAMMA = 0;
public static const int FXAA_COLOR_LINEAR = 1;
public static const int FXAA_COLOR_HDR = 2;

public struct FxaaSettings
{
    public float2 InvSourceSize;
    public float EdgeThreshold;
    public float EdgeThresholdMin;
    public float SubpixQuality;
    public float ColorVisibilityFloor;
};

static const float fxaaStepsLow[4] = { 1.0f, 2.0f, 4.0f, 8.0f };
static const float fxaaStepsMedium[7] = { 1.0f, 1.5f, 2.0f, 2.0f, 3.0f, 4.0f, 8.0f };
static const float fxaaStepsHigh[12] = { 1.0f, 1.0f, 1.5f, 2.0f, 2.0f, 2.0f, 2.0f, 3.0f, 4.0f, 6.0f, 8.0f, 12.0f };
static const float fxaaStepsUltra[16] = { 1.0f, 1.0f, 1.0f, 1.5f, 2.0f, 2.0f, 2.0f, 2.0f, 2.0f, 3.0f, 4.0f, 6.0f, 8.0f, 10.0f, 12.0f, 16.0f };

[ForceInline]
public int fxaaStepCount<let Quality : int>()
{
    if (Quality == FXAA_QUALITY_LOW) { return 4; }
    else if (Quality == FXAA_QUALITY_MEDIUM) { return 7; }
    else if (Quality == FXAA_QUALITY_ULTRA) { return 16; }
    else { return 12; }
}

[ForceInline]
public float fxaaStepDistance<let Quality : int>(int index)
{
    if (Quality == FXAA_QUALITY_LOW) { return fxaaStepsLow[index]; }
    else if (Quality == FXAA_QUALITY_MEDIUM) { return fxaaStepsMedium[index]; }
    else if (Quality == FXAA_QUALITY_ULTRA) { return fxaaStepsUltra[index]; }
    else { return fxaaStepsHigh[index]; }
}

[ForceInline]
public float fxaaComputeLuma<let ColorSpace : int>(float3 rgb)
{
    if (ColorSpace == FXAA_COLOR_LINEAR)
    {
        return sqrt(max(dot(rgb, float3(0.2126f, 0.7152f, 0.0722f)), 0.0f));
    }
    else if (ColorSpace == FXAA_COLOR_HDR)
    {
        float y = max(dot(rgb, float3(0.2126f, 0.7152f, 0.0722f)), 0.0f);
        return sqrt(y / (1.0f + y));
    }
    else
    {
        return dot(saturate(rgb), float3(0.299f, 0.587f, 0.114f));
    }
}

// Second-order planar discontinuity across the 4-neighborhood: identical to zero
// across any flat sloped plane (both orthographic and perspective 1/z), while
// preserving the full step magnitude on geometric silhouettes.
[ForceInline]
public bool fxaaDetectDepthCross(float dM, float dN, float dS, float dW, float dE, float thresholdMin, float thresholdRel)
{
    float discX = abs((dW + dE) - 2.0f * dM);
    float discY = abs((dN + dS) - 2.0f * dM);
    float disc = max(discX, discY);
    float threshold = max(thresholdMin, abs(dM) * thresholdRel);
    return disc > threshold;
}

[ForceInline]
public bool fxaaShouldProcessEdge<let Mode : int>(float range, float rangeMax, bool depthEdge, FxaaSettings settings)
{
    if (Mode == FXAA_MODE_DEPTH_ONLY)
    {
        return depthEdge && (range >= settings.ColorVisibilityFloor);
    }
    else if (Mode == FXAA_MODE_DEPTH_ASSISTED)
    {
        float lumaThreshold = max(settings.EdgeThresholdMin, rangeMax * settings.EdgeThreshold);
        return (range >= lumaThreshold) || (depthEdge && (range >= settings.ColorVisibilityFloor));
    }
    else
    {
        float lumaThreshold = max(settings.EdgeThresholdMin, rangeMax * settings.EdgeThreshold);
        return range >= lumaThreshold;
    }
}

[ForceInline]
public void fxaaAnalyzeDepthOrientation(
    float dNW, float dN, float dNE,
    float dW,  float dM, float dE,
    float dSW, float dS, float dSE,
    out bool horzSpan,
    out bool pairN)
{
    float depthHorz =
        0.25f * abs((dNW + dSW) - 2.0f * dW) +
        0.50f * abs((dN  + dS)  - 2.0f * dM) +
        0.25f * abs((dNE + dSE) - 2.0f * dE);
    float depthVert =
        0.25f * abs((dNW + dNE) - 2.0f * dN) +
        0.50f * abs((dW  + dE)  - 2.0f * dM) +
        0.25f * abs((dSW + dSE) - 2.0f * dS);

    horzSpan = depthHorz >= depthVert;
    float d1 = horzSpan ? dN : dW;
    float d2 = horzSpan ? dS : dE;
    pairN = abs(d1 - dM) >= abs(d2 - dM);
}

[ForceInline]
public float4 fxaaResolveEdge<let Quality : int, let Mode : int, let ColorSpace : int>(
    FxaaSettings settings,
    Texture2D<float4> source,
    float2 uv,
    float4 centerColor,
    float lumaM,
    float lumaN, float lumaS, float lumaW, float lumaE,
    float lumaNW, float lumaNE, float lumaSW, float lumaSE,
    float range,
    bool depthEdge,
    bool depthHorzSpan,
    bool depthPairN)
{
    float edgeHorz =
        abs((0.25f * lumaNW) + (-0.50f * lumaW) + (0.25f * lumaSW)) +
        abs((0.50f * lumaN)  + (-1.00f * lumaM) + (0.50f * lumaS)) +
        abs((0.25f * lumaNE) + (-0.50f * lumaE) + (0.25f * lumaSE));
    float edgeVert =
        abs((0.25f * lumaNW) + (-0.50f * lumaN) + (0.25f * lumaNE)) +
        abs((0.50f * lumaW)  + (-1.00f * lumaM) + (0.50f * lumaE)) +
        abs((0.25f * lumaSW) + (-0.50f * lumaS) + (0.25f * lumaSE));

    bool horzSpan = edgeHorz >= edgeVert;
    if (Mode != FXAA_MODE_LUMA)
    {
        if (depthEdge)
        {
            horzSpan = depthHorzSpan;
        }
    }

    float luma1 = horzSpan ? lumaN : lumaW;
    float luma2 = horzSpan ? lumaS : lumaE;
    float gradient1 = luma1 - lumaM;
    float gradient2 = luma2 - lumaM;

    bool is1Steepest = abs(gradient1) >= abs(gradient2);
    if (Mode != FXAA_MODE_LUMA)
    {
        if (depthEdge)
        {
            is1Steepest = depthPairN;
        }
    }

    float stepLength = horzSpan ? -settings.InvSourceSize.y : -settings.InvSourceSize.x;
    float lumaLocalAverage;
    float gradientAbs;
    if (is1Steepest)
    {
        lumaLocalAverage = 0.5f * (luma1 + lumaM);
        gradientAbs = abs(gradient1);
    }
    else
    {
        stepLength = -stepLength;
        lumaLocalAverage = 0.5f * (luma2 + lumaM);
        gradientAbs = abs(gradient2);
    }

    float gradientScaled = 0.25f * gradientAbs;
    if (Mode != FXAA_MODE_LUMA)
    {
        if (depthEdge)
        {
            gradientScaled = max(gradientScaled, 0.25f * settings.ColorVisibilityFloor);
        }
    }

    float2 currentUv = uv;
    if (horzSpan)
    {
        currentUv.y += stepLength * 0.5f;
    }
    else
    {
        currentUv.x += stepLength * 0.5f;
    }

    float2 offset = horzSpan ? float2(settings.InvSourceSize.x, 0.0f) : float2(0.0f, settings.InvSourceSize.y);
    SamplerState linearClamp = shared_sampler_linear_clamp();

    float2 uv1 = currentUv - offset;
    float2 uv2 = currentUv + offset;
    float dist1 = 1.0f;
    float dist2 = 1.0f;

    float lumaEnd1 = fxaaComputeLuma<ColorSpace>(source.SampleLevel(linearClamp, uv1, 0.0f).rgb) - lumaLocalAverage;
    float lumaEnd2 = fxaaComputeLuma<ColorSpace>(source.SampleLevel(linearClamp, uv2, 0.0f).rgb) - lumaLocalAverage;

    bool reached1 = abs(lumaEnd1) >= gradientScaled;
    bool reached2 = abs(lumaEnd2) >= gradientScaled;
    if (!(reached1 && reached2))
    {
        int stepCount = fxaaStepCount<Quality>();
        [unroll]
        for (int i = 1; i < stepCount; ++i)
        {
            float stepDist = fxaaStepDistance<Quality>(i);
            if (!reached1)
            {
                uv1 -= offset * stepDist;
                dist1 += stepDist;
                lumaEnd1 = fxaaComputeLuma<ColorSpace>(source.SampleLevel(linearClamp, uv1, 0.0f).rgb) - lumaLocalAverage;
                reached1 = abs(lumaEnd1) >= gradientScaled;
            }
            if (!reached2)
            {
                uv2 += offset * stepDist;
                dist2 += stepDist;
                lumaEnd2 = fxaaComputeLuma<ColorSpace>(source.SampleLevel(linearClamp, uv2, 0.0f).rgb) - lumaLocalAverage;
                reached2 = abs(lumaEnd2) >= gradientScaled;
            }
            if (reached1 && reached2)
            {
                break;
            }
        }
    }

    bool isDirection1 = dist1 < dist2;
    float distanceFinal = min(dist1, dist2);
    float edgeThickness = dist1 + dist2;
    float pixelOffset = (-distanceFinal / edgeThickness) + 0.5f;

    bool isLumaCenterSmaller = lumaM < lumaLocalAverage;
    bool correctVariation = ((isDirection1 ? lumaEnd1 : lumaEnd2) < 0.0f) != isLumaCenterSmaller;
    float finalOffset = correctVariation ? pixelOffset : 0.0f;

    bool applySubpix = settings.SubpixQuality > 0.0f;
    if (Mode == FXAA_MODE_DEPTH_ONLY)
    {
        applySubpix = false;
    }
    else if (Mode == FXAA_MODE_DEPTH_ASSISTED)
    {
        if (depthEdge)
        {
            applySubpix = false;
        }
    }

    if (applySubpix)
    {
        float lumaAverage = (2.0f * (lumaN + lumaS + lumaW + lumaE) + (lumaNW + lumaNE + lumaSW + lumaSE)) * (1.0f / 12.0f);
        float subpix1 = saturate(abs(lumaAverage - lumaM) / max(range, 1e-6f));
        float subpix2 = (-2.0f * subpix1 + 3.0f) * subpix1 * subpix1;
        float subpixOffset = subpix2 * subpix2 * (0.5f * settings.SubpixQuality);
        finalOffset = max(finalOffset, subpixOffset);
    }

    if (finalOffset <= 0.0f)
    {
        return centerColor;
    }

    float2 finalUv = uv;
    if (horzSpan)
    {
        finalUv.y += finalOffset * stepLength;
    }
    else
    {
        finalUv.x += finalOffset * stepLength;
    }

    float3 finalRgb = source.SampleLevel(linearClamp, finalUv, 0.0f).rgb;
    return float4(finalRgb, centerColor.a);
}
