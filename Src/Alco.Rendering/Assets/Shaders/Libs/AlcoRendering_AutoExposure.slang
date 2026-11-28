// file "AlcoRendering_AutoExposure.slang" - module "AlcoRendering_AutoExposure"
//
// Shared constants, GPU state payload, luminance quantization, and metering
// helpers for 256-bin log2 histogram Auto Exposure.

#language slang 2025
module AlcoRendering_AutoExposure;

public static const uint AUTO_EXPOSURE_BIN_COUNT = 256u;
public static const uint AUTO_EXPOSURE_HISTOGRAM_GROUP_DIM = 16u;
public static const float AUTO_EXPOSURE_WEIGHT_SCALE = 256.0f;

public static const uint AUTO_EXPOSURE_METERING_UNIFORM = 0u;
public static const uint AUTO_EXPOSURE_METERING_CENTER_WEIGHTED = 1u;
public static const uint AUTO_EXPOSURE_METERING_SPOT = 2u;

public static const uint AUTO_EXPOSURE_FLAG_FORCE_RESET = 1u << 0;
public static const uint AUTO_EXPOSURE_FLAG_IGNORE_DARK_FLOOR = 1u << 1;

public struct AutoExposureState
{
    public float Exposure;
    public float InvExposure;
    public float AdaptedEV;
    public float TargetEV;
    public float AverageLuminance;
    public uint SampledWeight;
    public uint Initialized;
    public uint Pad0;
};

[ForceInline]
public float autoExposureLuminance(float3 rgb)
{
    return dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
}

[ForceInline]
public uint autoExposureLuminanceToBin(float luminance, float minLogLum, float invLogLumRange)
{
    if (!(luminance > 0.0f) || !isfinite(luminance))
    {
        return 0u;
    }

    float logLum = log2(luminance);
    if (logLum <= minLogLum)
    {
        return 0u;
    }

    float t = saturate((logLum - minLogLum) * invLogLumRange);
    return clamp(uint(t * 254.0f + 0.5f) + 1u, 1u, AUTO_EXPOSURE_BIN_COUNT - 1u);
}

[ForceInline]
public float autoExposureBinToLogLuminance(uint bin, float minLogLum, float logLumRange)
{
    if (bin == 0u)
    {
        return minLogLum;
    }

    return minLogLum + (float(bin - 1u) / 254.0f) * logLumRange;
}

[ForceInline]
public uint autoExposureMeteringWeight(float2 uv, uint meteringMode, float centerRadius)
{
    if (meteringMode == AUTO_EXPOSURE_METERING_UNIFORM)
    {
        return uint(AUTO_EXPOSURE_WEIGHT_SCALE);
    }

    float2 d = (uv - 0.5f) * 2.0f;
    float r = length(d) / max(centerRadius, 0.05f);
    if (meteringMode == AUTO_EXPOSURE_METERING_SPOT)
    {
        float w = saturate(1.0f - smoothstep(0.6f, 1.0f, r));
        return uint(w * AUTO_EXPOSURE_WEIGHT_SCALE + 0.5f);
    }

    float w = 0.2f + 0.8f * exp(-r * r * 1.5f);
    return max(uint(w * AUTO_EXPOSURE_WEIGHT_SCALE + 0.5f), 1u);
}
