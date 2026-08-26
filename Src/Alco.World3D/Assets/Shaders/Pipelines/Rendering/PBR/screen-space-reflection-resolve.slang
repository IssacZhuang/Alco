// file "screen-space-reflection-resolve.slang" - module "screen_space_reflection_resolve"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/ScreenSpaceReflectionResolve.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module screen_space_reflection_resolve;

import alco_world3d_ssr_post_common;
import alco_world3d_reversed_depth;
import alco_rendering_core;

// alco_world3d_ssr_post_common declares ssrData; this pass's ParameterBlock follows
// it in declaration order.
public struct PassParams
{
    public Texture2D reflectionRaw;
    public Texture2D reflectionHistory;
    public Texture2D historyMetadata;
    public Texture2D normal;
    public DepthTexture2D gbufferDepth;
};

public ParameterBlock<PassParams> pass;


[shader("vertex")]
V2F MainVS(Vertex input)
{
    return ssrPostFullQuadVS(input);
}

[shader("fragment")]
void MainPS(
    V2F input,
    out float4 reflectionOut : SV_TARGET0,
    out float4 metadataOut : SV_TARGET1)
{
    float2 fullSize = float2(ssrData.fullWidth, ssrData.fullHeight);
    float2 traceSize = float2(ssrData.traceWidth, ssrData.traceHeight);
    int2 centerPixel = clamp(
        int2(input.uv * fullSize), int2(0, 0), int2(fullSize) - 1);
    float centerDepth = getPixelDepth2D(pass.gbufferDepth, centerPixel);
    if (isSkyDepth(centerDepth))
    {
        reflectionOut = 0.0;
        metadataOut = 0.0;
        return;
    }

    float3 centerNormal = normalize(
        pass.normal.Load(int3(centerPixel, 0)).xyz * 2.0 - 1.0);
    // Resolve and history share the trace grid, so input.uv must remain the
    // canonical receiver coordinate used by temporal reprojection.
    float3 centerWorld = ssrPostReconstructWorldPosition(input.uv, centerDepth);
    float centerDistance = length(centerWorld - ssrData.ssrCameraPosition.xyz);

    // Bilateral 5x5 filter. Plane distance is used instead of full world-space
    // distance so neighbouring samples on a floor remain eligible at grazing
    // angles while samples across geometry edges are still rejected.
    float3 colorSum = 0.0;
    float confidenceSum = 0.0;
    float weightSum = 0.0;
    float3 neighborhoodMin = float3(65504.0, 65504.0, 65504.0);
    float3 neighborhoodMax = 0.0;
    float2 traceTexel = rcp(traceSize);
    [loop]
    for (int y = -2; y <= 2; y++)
    {
        [loop]
        for (int x = -2; x <= 2; x++)
        {
            float2 sampleUV = clamp(
                input.uv + float2(x, y) * traceTexel, 0.0, 1.0);
            float4 sampleReflection = pass.reflectionRaw.Sample(samplers.linearClamp, sampleUV);
            int2 samplePixel = clamp(
                int2(sampleUV * fullSize), int2(0, 0), int2(fullSize) - 1);
            float sampleDepth = getPixelDepth2D(pass.gbufferDepth, samplePixel);
            if (isSkyDepth(sampleDepth))
            {
                continue;
            }

            float3 sampleNormal = normalize(
                pass.normal.Load(int3(samplePixel, 0)).xyz * 2.0 - 1.0);
            float3 sampleWorld = ssrPostReconstructWorldPosition(
                sampleUV, sampleDepth);
            float normalWeight = pow(
                saturate(dot(centerNormal, sampleNormal)), 24.0);
            float planeDistance = abs(dot(
                sampleWorld - centerWorld, centerNormal));
            float planeScale = 0.025 + centerDistance * 0.003;
            float depthWeight = exp(-planeDistance / planeScale);
            float spatialWeight = exp(-float(x * x + y * y) / 8.0);
            float weight = normalWeight * depthWeight * spatialWeight;

            colorSum += sampleReflection.rgb * sampleReflection.a * weight;
            confidenceSum += sampleReflection.a * weight;
            weightSum += weight;
            if (sampleReflection.a > 0.01 && weight > 0.01)
            {
                neighborhoodMin = min(neighborhoodMin, sampleReflection.rgb);
                neighborhoodMax = max(neighborhoodMax, sampleReflection.rgb);
            }
        }
    }

    float confidence = confidenceSum / max(weightSum, 1e-5);
    float3 color = confidenceSum > 1e-5
        ? colorSum / confidenceSum
        : 0.0;
    bool currentHit = confidence > 0.01;

    float previousSampleCount = 0.0;
    float historyMotion = 0.0;
    float4 history = 0.0;
    bool historyValid = false;
    if (ssrData.historyValid)
    {
        float4 previousClip = mul(
            ssrData.ssrPreviousViewProjection, float4(centerWorld, 1.0));
        if (previousClip.w > 0.0)
        {
            float3 previousNdc = previousClip.xyz / previousClip.w;
            float2 previousUV = float2(
                previousNdc.x * 0.5 + 0.5,
                0.5 - previousNdc.y * 0.5);
            if (all(previousUV >= 0.0) && all(previousUV <= 1.0)
                && previousNdc.z >= 0.0 && previousNdc.z <= 1.0)
            {
                history = pass.reflectionHistory.Sample(samplers.linearClamp, previousUV);
                float4 previousMetadata = pass.historyMetadata.Sample(
                    samplers.linearClamp, previousUV);
                float3 previousNormal = ssrPostDecodeNormal(
                    previousMetadata.xy);
                // Metadata stores linear receiver distance. R16F preserves this
                // far more accurately than non-linear NDC depth, whose half-float
                // quantisation was invalidating history on distant surfaces.
                float expectedPreviousDistance = length(
                    centerWorld - ssrData.ssrPreviousCameraPosition.xyz);
                float positionTolerance = 0.04
                    + expectedPreviousDistance * 0.005;
                historyValid = previousMetadata.w > 0.5
                    && dot(centerNormal, previousNormal) > 0.96
                    && abs(previousMetadata.z - expectedPreviousDistance)
                        < positionTolerance;
                if (historyValid)
                {
                    previousSampleCount = previousMetadata.w;
                    float receiverMotionPixels = length(
                        (previousUV - input.uv) * fullSize);
                    float cameraMotion = length(
                        ssrData.ssrCameraPosition.xyz
                        - ssrData.ssrPreviousCameraPosition.xyz);
                    historyMotion = saturate(
                        receiverMotionPixels * 0.15 + cameraMotion * 1.5);
                }
            }
        }
    }

    float nextSampleCount = currentHit ? 1.0 : 0.0;
    if (historyValid)
    {
        if (currentHit)
        {
            // Clip reprojected radiance to a relaxed current neighbourhood to
            // prevent disocclusion ghosts without destroying glossy variance.
            if (neighborhoodMin.x < 65503.0)
            {
                float3 extent = max(
                    neighborhoodMax - neighborhoodMin, 0.02);
                history.rgb = clamp(
                    history.rgb,
                    neighborhoodMin - extent * 0.35,
                    neighborhoodMax + extent * 0.35);
            }

            // Stationary pixels keep integrating to 256 samples. Motion lowers
            // the effective history length and raises the current-frame weight
            // so reflections respond promptly instead of leaving trails.
            float maximumSamples = lerp(256.0, 8.0, historyMotion);
            nextSampleCount = min(
                previousSampleCount + 1.0, maximumSamples);
            float currentWeight = max(
                rcp(nextSampleCount),
                lerp(1.0 / 256.0, 0.125, historyMotion));
            color = lerp(history.rgb, color, currentWeight);
            confidence = lerp(history.a, confidence, currentWeight);
        }
        else
        {
            // A stochastic miss must not punch a bright one-frame hole. Retain
            // valid history briefly, but decay it so real lost intersections
            // disappear in a bounded number of frames.
            color = history.rgb;
            confidence = history.a * 0.94;
            nextSampleCount = max(previousSampleCount - 1.0, 1.0);
        }
    }

    reflectionOut = float4(max(color, 0.0), saturate(confidence));
    metadataOut = float4(
        ssrPostEncodeNormal(centerNormal), centerDistance, nextSampleCount);
}
