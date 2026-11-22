// file "AlcoRendering_Tonemap.slang" - module "AlcoRendering_Tonemap"
//
// Shared Tonemap constants, parameter payload, ACES fitted & Khronos PBR Neutral
// operators, gamma correction, and 8-bit IGN output dither.

#language slang 2025
module AlcoRendering_Tonemap;

public static const uint TONEMAP_THREAD_GROUP_DIM = 8u;

public static const int TONEMAP_OPERATOR_LINEAR = 0;
public static const int TONEMAP_OPERATOR_ACES = 1;
public static const int TONEMAP_OPERATOR_NEUTRAL = 2;

public struct TonemapParams
{
    public float Exposure;
    public float Gamma;
    public float AcesA;
    public float AcesB;
    public float AcesC;
    public float AcesD;
    public float AcesE;
    public float NeutralStartCompression;
    public float NeutralDesaturation;
};

[ForceInline]
public float3 tonemapSanitizeHdr(float3 color)
{
    if (any(isnan(color)) || any(isinf(color)))
    {
        color.r = isnan(color.r) ? 0.0f : clamp(color.r, -1e6f, 1e6f);
        color.g = isnan(color.g) ? 0.0f : clamp(color.g, -1e6f, 1e6f);
        color.b = isnan(color.b) ? 0.0f : clamp(color.b, -1e6f, 1e6f);
    }
    return color;
}

[ForceInline]
public float3 tonemapAces(float3 hdr, float a, float b, float c, float d, float e, float exposure)
{
    float3 x = hdr * exposure;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0f, 1.0f);
}

// Khronos PBR Neutral tone mapping (ported from pbrNeutral.glsl)
// Reference: https://github.com/KhronosGroup/ToneMapping/blob/main/PBR_Neutral/pbrNeutral.glsl
[ForceInline]
public float3 tonemapNeutral(float3 hdr, float exposure, float startCompression, float desaturation)
{
    float3 color = hdr * exposure;

    // Toe & offset
    float x = min(color.r, min(color.g, color.b));
    float offset = (x < 0.08f) ? (x - 6.25f * x * x) : 0.04f;
    color -= offset;

    // If below compression start, return early
    float peak = max(color.r, max(color.g, color.b));
    if (peak < startCompression)
    {
        return color;
    }

    // Compress peak to fit in [0,1]
    float d = 1.0f - startCompression;
    float denom = max(peak + d - startCompression, 1e-6f);
    float newPeak = 1.0f - (d * d) / denom;
    color *= (newPeak / max(peak, 1e-6f));

    // Desaturate towards white based on removed brightness
    float g = 1.0f - 1.0f / max(desaturation * (peak - newPeak) + 1.0f, 1e-6f);
    float3 white = float3(newPeak, newPeak, newPeak);
    return lerp(color, white, g);
}

[ForceInline]
public float3 tonemapApplyGamma(float3 ldr, float gamma)
{
    ldr = max(ldr, 0.0f);
    if (abs(gamma - 1.0f) <= 1e-5f)
    {
        return ldr;
    }

    float invGamma = 1.0f / max(gamma, 1e-4f);
    ldr.r = ldr.r > 0.0f ? pow(ldr.r, invGamma) : 0.0f;
    ldr.g = ldr.g > 0.0f ? pow(ldr.g, invGamma) : 0.0f;
    ldr.b = ldr.b > 0.0f ? pow(ldr.b, invGamma) : 0.0f;
    return ldr;
}

// Triangular-PDF dither based on interleaved gradient noise (Jimenez 2014),
// scaled to +/-1 code of an 8-bit UNORM target.
[ForceInline]
public float tonemapOutputDither8Bit(float2 pixelPos)
{
    const float2 k = float2(0.06711056f, 0.00583715f);
    float r1 = frac(52.9829189f * frac(dot(pixelPos, k)));
    float r2 = frac(52.9829189f * frac(dot(pixelPos + 0.5f, k)));
    return (r1 + r2 - 1.0f) * (1.0f / 255.0f);
}

[ForceInline]
public float4 tonemapEvaluate<let Operator : int, let EnableDither : int>(
    float4 sampled,
    float2 pixelPos,
    TonemapParams p)
{
    if (Operator == TONEMAP_OPERATOR_LINEAR)
    {
        return sampled;
    }

    float3 hdr = tonemapSanitizeHdr(sampled.rgb);
    float3 ldr;
    if (Operator == TONEMAP_OPERATOR_ACES)
    {
        ldr = tonemapAces(hdr, p.AcesA, p.AcesB, p.AcesC, p.AcesD, p.AcesE, p.Exposure);
    }
    else
    {
        ldr = tonemapNeutral(hdr, p.Exposure, p.NeutralStartCompression, p.NeutralDesaturation);
    }

    ldr = tonemapApplyGamma(ldr, p.Gamma);
    if (EnableDither != 0)
    {
        ldr += tonemapOutputDither8Bit(pixelPos);
    }

    return float4(ldr, 1.0f);
}
