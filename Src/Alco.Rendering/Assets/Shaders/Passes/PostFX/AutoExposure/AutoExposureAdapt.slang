// file "AutoExposureAdapt.slang" - module "AutoExposureAdapt"
//
// Reduces the 256-bin luminance histogram with percentile clipping and updates
// the persistent AutoExposureState buffer using asymmetric EV adaptation.

#language slang 2025
module AutoExposureAdapt;

import Core;
import AlcoRendering_AutoExposure;

[GPURoot]
public struct AutoExposureAdaptRoot
{
    public GPUBufferView Histogram;
    public GPUBufferView State;
    public float MinLogLuminance;
    public float LogLuminanceRange;
    public float LowPercent;
    public float HighPercent;
    public float MinEV;
    public float MaxEV;
    public float ExposureCompensation;
    public float KeyValue;
    public float SpeedUp;
    public float SpeedDown;
    public float DeltaTime;
    public uint Flags;
};

groupshared uint s_Bins[AUTO_EXPOSURE_BIN_COUNT];

[shader("compute")]
[numthreads(AUTO_EXPOSURE_BIN_COUNT, 1, 1)]
void mainCS(uint groupIndex : SV_GroupIndex)
{
    AutoExposureAdaptRoot root = gpu_load_root<AutoExposureAdaptRoot>();
    s_Bins[groupIndex] = gpu_load<uint>(root.Histogram, groupIndex);
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex != 0u)
    {
        return;
    }

    bool ignoreDarkFloor = (root.Flags & AUTO_EXPOSURE_FLAG_IGNORE_DARK_FLOOR) != 0u;
    uint startBin = ignoreDarkFloor ? 1u : 0u;
    float totalWeight = 0.0f;
    for (uint b = startBin; b < AUTO_EXPOSURE_BIN_COUNT; ++b)
    {
        totalWeight += float(s_Bins[b]);
    }

    AutoExposureState prev = gpu_load<AutoExposureState>(root.State, 0u);
    bool forceReset = (root.Flags & AUTO_EXPOSURE_FLAG_FORCE_RESET) != 0u
        || prev.Initialized == 0u
        || !isfinite(prev.AdaptedEV)
        || !isfinite(prev.Exposure)
        || prev.Exposure <= 0.0f;

    float minEV = min(root.MinEV, root.MaxEV);
    float maxEV = max(root.MinEV, root.MaxEV);
    float prevEV = forceReset ? 0.0f : clamp(prev.AdaptedEV, minEV, maxEV);
    float logRange = max(root.LogLuminanceRange, 1e-3f);

    float avgLuminance = 0.0f;
    float targetEV;
    if (totalWeight <= 0.0f)
    {
        targetEV = forceReset ? clamp(-root.ExposureCompensation, minEV, maxEV) : prevEV;
    }
    else
    {
        float lowP = saturate(root.LowPercent);
        float highP = saturate(max(root.HighPercent, lowP));
        float lowLimit = totalWeight * lowP;
        float highLimit = totalWeight * highP;

        float running = 0.0f;
        float weightedLogSum = 0.0f;
        float keptWeight = 0.0f;
        uint fallbackBin = startBin;
        bool foundFallback = false;

        for (uint b = startBin; b < AUTO_EXPOSURE_BIN_COUNT; ++b)
        {
            float w = float(s_Bins[b]);
            if (w <= 0.0f)
            {
                continue;
            }

            float nextRunning = running + w;
            if (!foundFallback && nextRunning >= lowLimit)
            {
                fallbackBin = b;
                foundFallback = true;
            }

            float overlapStart = max(running, lowLimit);
            float overlapEnd = min(nextRunning, highLimit);
            if (overlapEnd > overlapStart)
            {
                float span = overlapEnd - overlapStart;
                weightedLogSum += autoExposureBinToLogLuminance(b, root.MinLogLuminance, logRange) * span;
                keptWeight += span;
            }

            running = nextRunning;
        }

        float avgLogLum = keptWeight > 0.0f
            ? (weightedLogSum / keptWeight)
            : autoExposureBinToLogLuminance(fallbackBin, root.MinLogLuminance, logRange);
        avgLuminance = exp2(avgLogLum);
        float rawEV = avgLogLum - log2(max(root.KeyValue, 1e-4f)) - root.ExposureCompensation;
        targetEV = clamp(rawEV, minEV, maxEV);
    }

    float adaptedEV;
    if (forceReset)
    {
        adaptedEV = targetEV;
    }
    else if (root.DeltaTime <= 0.0f)
    {
        adaptedEV = prevEV;
    }
    else
    {
        float speed = (targetEV > prevEV) ? max(root.SpeedUp, 0.0f) : max(root.SpeedDown, 0.0f);
        float alpha = 1.0f - exp(-root.DeltaTime * speed);
        adaptedEV = lerp(prevEV, targetEV, saturate(alpha));
    }

    adaptedEV = clamp(adaptedEV, minEV, maxEV);
    float exposure = exp2(-adaptedEV);
    float invExposure = 1.0f / max(exposure, 1e-6f);

    AutoExposureState nextState;
    nextState.Exposure = exposure;
    nextState.InvExposure = invExposure;
    nextState.AdaptedEV = adaptedEV;
    nextState.TargetEV = targetEV;
    nextState.AverageLuminance = avgLuminance;
    nextState.SampledWeight = uint(min(totalWeight, 4294967000.0f));
    nextState.Initialized = 1u;
    nextState.Pad0 = 0u;
    gpu_store<AutoExposureState>(root.State, 0u, nextState);
}
