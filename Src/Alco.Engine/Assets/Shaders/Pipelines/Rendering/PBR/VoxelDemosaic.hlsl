#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Full-resolution reconstruction and temporal accumulation for voxel GI. Reads
// the half-resolution cone-traced atlas (diffuse left, specular right), performs
// a depth/normal-aware 2x2 upscale in linear world distance, and validates
// reprojected history against stored clip-space w plus the surface normal at
// the reprojected G-buffer location. Specular alpha stores history depth; the
// deferred lighting pass consumes only specular rgb.
//
// Both the indirect atlas (sampled by DeferredLighting) and a history texture
// (read by this shader next frame) are written in the same dispatch.

struct VoxelDemosaicConstants
{
    float4 params; // x=hysteresis (0..1), y=spatialSigma, z=unused, w=unused
};

DEFINE_TEX2D_READ(1, _traceInput);
DEFINE_TEX2D_DEPTH(2, _gbufferDepth);
DEFINE_TEX2D_READ(3, _normal);
DEFINE_TEX2D_STORAGE(4, _indirectGI, float4, "rgba16f");
DEFINE_TEX2D_READ(5, _historyInput);
DEFINE_TEX2D_STORAGE(6, _historyOut, float4, "rgba16f");

PUSH_CONSTANT VoxelDemosaicConstants constants;

float3 ReconstructWorldPosition(float2 uv, float depth, float4x4 invVP)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invVP, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 gbufferRes = uint2(giParams2.y, giParams2.z);
    uint2 atlasRes = uint2(gbufferRes.x * 2u, gbufferRes.y);
    if (any(dispatchId.xy >= atlasRes))
    {
        return;
    }

    uint2 atlasPixel = dispatchId.xy;
    bool isSpecular = atlasPixel.x >= gbufferRes.x;
    uint2 gbufferPixel = uint2(isSpecular ? atlasPixel.x - gbufferRes.x : atlasPixel.x, atlasPixel.y);
    float2 gbufferUV = (float2(gbufferPixel) + 0.5) / float2(gbufferRes);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    if (depth >= 0.9999)
    {
        _indirectGI[atlasPixel] = 0.0;
        _historyOut[atlasPixel] = 0.0;
        return;
    }

    float3 N = normalize(GET_PIXEL_TEX2D(_normal, gbufferPixel).xyz * 2.0 - 1.0);
    float3 worldPos = ReconstructWorldPosition(gbufferUV, depth, invViewProjection);
    float linearDistance = length(worldPos - cameraPosition.xyz);
    int2 traceRes = int2(giParams.z, giParams.w);
    float2 tracePosition = (float2(gbufferPixel) + 0.5) * float2(traceRes) / float2(gbufferRes) - 0.5;
    int2 traceBase = int2(floor(tracePosition));
    int2 nearestTracePixel = clamp(int2(floor(tracePosition + 0.5)), int2(0, 0), traceRes - 1);
    int2 nearestTraceAtlasPixel = nearestTracePixel
        + (isSpecular ? int2(traceRes.x, 0) : int2(0, 0));
    float4 nearestTraceValue = _traceInput.Load(int3(nearestTraceAtlasPixel, 0));
    float4 spatialSum = 0.0;
    float spatialWeight = 0.0;

    // Bilinear spatial weights retain detail on continuous surfaces. Relative
    // linear distance and normal weights prevent foreground/background mixing,
    // including at far depth where raw device-depth deltas collapse.
    [unroll]
    for (int oy = 0; oy <= 1; oy++)
    {
        [unroll]
        for (int ox = 0; ox <= 1; ox++)
        {
            int2 tracePixel = clamp(traceBase + int2(ox, oy), int2(0, 0), traceRes - 1);
            float2 traceUV = (float2(tracePixel) + 0.5) / float2(traceRes);
            int2 sampleGbufferPixel = clamp(
                int2(traceUV * float2(gbufferRes)),
                int2(0, 0),
                int2(gbufferRes) - 1);
            float sampleDepth = GET_PIXEL_TEX2D(_gbufferDepth, sampleGbufferPixel);
            if (sampleDepth >= 0.9999)
            {
                continue;
            }

            float3 sampleNormal = normalize(GET_PIXEL_TEX2D(_normal, sampleGbufferPixel).xyz * 2.0 - 1.0);
            float2 sampleGbufferUV = (float2(sampleGbufferPixel) + 0.5) / float2(gbufferRes);
            float3 sampleWorldPos = ReconstructWorldPosition(sampleGbufferUV, sampleDepth, invViewProjection);
            float sampleLinearDistance = length(sampleWorldPos - cameraPosition.xyz);
            float relativeDepth = abs(sampleLinearDistance - linearDistance) / max(linearDistance, 0.001);
            float depthWeight = saturate((0.12 - relativeDepth) * 4.0) + 0.001;
            float normalExp = isSpecular ? 16.0 : 4.0;
            float normalWeight = pow(max(dot(N, sampleNormal), 0.0), normalExp);
            float2 bilinearDelta = abs(tracePosition - float2(tracePixel));
            float bilinearWeight = max((1.0 - bilinearDelta.x) * (1.0 - bilinearDelta.y), 0.001);
            float weight = bilinearWeight * depthWeight * normalWeight;
            int2 traceAtlasPixel = tracePixel + (isSpecular ? int2(traceRes.x, 0) : int2(0, 0));
            spatialSum += _traceInput.Load(int3(traceAtlasPixel, 0)) * weight;
            spatialWeight += weight;
        }
    }
    float4 current = spatialWeight > 0.0001
        ? spatialSum / spatialWeight
        : nearestTraceValue;

    // --- Temporal reprojection ---
    float4 result = current;
    bool historyAvailable = giFrameParams.z > 0.5;

    if (historyAvailable)
    {
        float4 prevClip = mul(viewProjectionPrev, float4(worldPos, 1.0));
        if (prevClip.w > 0.0)
        {
            float2 prevNDC = float2(prevClip.x / prevClip.w, prevClip.y / prevClip.w);
            float2 prevUV = float2(prevNDC.x * 0.5 + 0.5, 0.5 - prevNDC.y * 0.5);

            if (all(prevUV >= 0.0) && all(prevUV <= 1.0))
            {
                int2 previousSurfacePixel = clamp(
                    int2(prevUV * float2(gbufferRes)),
                    int2(0, 0),
                    int2(gbufferRes) - 1);
                int2 previousSpecularPixel = previousSurfacePixel + int2(gbufferRes.x, 0);
                float historyClipW = _historyInput.Load(int3(previousSpecularPixel, 0)).a;
                float relativePreviousDepth = abs(historyClipW - prevClip.w) / max(abs(prevClip.w), 0.001);
                float currentPreviousDepth = GET_PIXEL_TEX2D(_gbufferDepth, previousSurfacePixel);
                float3 currentPreviousNormal = normalize(
                    GET_PIXEL_TEX2D(_normal, previousSurfacePixel).xyz * 2.0 - 1.0);
                bool depthValid = historyClipW > 0.0
                    && currentPreviousDepth < 0.9999
                    && relativePreviousDepth < 0.03;
                bool normalValid = dot(N, currentPreviousNormal) > 0.85;
                if (depthValid && normalValid)
                {
                    int2 historyPixel = previousSurfacePixel
                        + (isSpecular ? int2(gbufferRes.x, 0) : int2(0, 0));
                    float4 history = _historyInput.Load(int3(historyPixel, 0));
                    float currentLuminance = dot(current.rgb, float3(0.299, 0.587, 0.114));
                    float historyLuminance = dot(history.rgb, float3(0.299, 0.587, 0.114));
                    float luminanceChange = abs(currentLuminance - historyLuminance)
                        / max(max(currentLuminance, historyLuminance), 0.001);
                    float visibilityChange = isSpecular ? 0.0 : abs(current.a - history.a);
                    float change = max(saturate(luminanceChange * 3.0), saturate(visibilityChange * 4.0));
                    float blendRate = lerp(1.0 - constants.params.x, 1.0, change);
                    result = lerp(history, current, blendRate);
                }
            }
        }
    }

    if (isSpecular)
    {
        result.a = mul(viewProjection, float4(worldPos, 1.0)).w;
    }
    _indirectGI[atlasPixel] = result;
    _historyOut[atlasPixel] = result;
}
