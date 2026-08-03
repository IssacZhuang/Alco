#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/VoxelCommon.hlsli"

// Half-resolution bilateral spatial filter and temporal accumulation for
// voxel GI. Reads the half-resolution cone-traced atlas (diffuse left half,
// specular right half), applies a depth-aware 3x3 bilateral filter, and
// blends with reprojected history using exponential hysteresis with change
// clipping to suppress ghosting.
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
    uint2 atlasRes = uint2(giParams.z * 2u, giParams.w);
    if (any(dispatchId.xy >= atlasRes))
    {
        return;
    }

    uint2 pixel = dispatchId.xy;
    bool isSpecular = pixel.x >= giParams.z;
    int halfWidth = (int)giParams.z;

    // Map atlas pixel to the half-res trace pixel and then to full-res G-buffer.
    int2 tracePixel = isSpecular ? int2(pixel.x - halfWidth, pixel.y) : int2(pixel.xy);
    tracePixel = clamp(tracePixel, int2(0, 0), int2(halfWidth - 1, (int)giParams.w - 1));

    uint2 gbufferRes = uint2(giParams2.y, giParams2.z);
    float2 gbufferUV = (float2(tracePixel) + 0.5) / float2(giParams.z, giParams.w);
    int2 gbufferPixel = int2(gbufferUV * float2(gbufferRes));
    gbufferPixel = clamp(gbufferPixel, int2(0, 0), int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));

    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    if (depth >= 0.9999)
    {
        _indirectGI[pixel] = float4(0.0, 0.0, 0.0, 0.0);
        _historyOut[pixel] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float3 N = normalize(GET_PIXEL_TEX2D(_normal, gbufferPixel).xyz * 2.0 - 1.0);
    float3 worldPos = ReconstructWorldPosition(gbufferUV, depth, invViewProjection);

    // --- Bilateral 3x3 spatial filter on the trace input ---
    float spatialSigma = max(constants.params.y, 0.001);
    float depthScale = 50.0 / spatialSigma;

    float4 centerVal = _traceInput.Load(int3(pixel, 0));
    float4 spatialSum = centerVal;
    float spatialW = 1.0;

    [unroll]
    for (int dy = -1; dy <= 1; dy++)
    {
        [unroll]
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0)
            {
                continue;
            }

            int2 np = int2(pixel) + int2(dx, dy);
            // Keep within same atlas half (diffuse or specular).
            if (isSpecular)
            {
                np.x = clamp(np.x, halfWidth, (int)atlasRes.x - 1);
            }
            else
            {
                np.x = clamp(np.x, 0, halfWidth - 1);
            }
            np.y = clamp(np.y, 0, (int)atlasRes.y - 1);

            // G-buffer depth at the neighbour for bilateral weighting.
            int2 nTrace = isSpecular ? np - int2(halfWidth, 0) : np;
            float2 nUV = (float2(nTrace) + 0.5) / float2(giParams.z, giParams.w);
            int2 nGbufPixel = int2(nUV * float2(gbufferRes));
            nGbufPixel = clamp(nGbufPixel, int2(0, 0), int2((int)gbufferRes.x - 1, (int)gbufferRes.y - 1));
            float nDepth = GET_PIXEL_TEX2D(_gbufferDepth, nGbufPixel);

            float depthW = exp(-abs(nDepth - depth) * depthScale);
            float spatialW_neighbour = exp(-(dx * dx + dy * dy) / (2.0 * spatialSigma * spatialSigma));

            // Normal-based bilateral weight: reject neighbours on different-
            // facing surfaces. Higher exponent for specular preserves
            // reflection sharpness on curved geometry.
            float3 nNormal = normalize(GET_PIXEL_TEX2D(_normal, nGbufPixel).xyz * 2.0 - 1.0);
            float normalExp = isSpecular ? 16.0 : 4.0;
            float normalW = pow(max(dot(N, nNormal), 0.0), normalExp);

            float w = depthW * spatialW_neighbour * normalW;
            spatialSum += _traceInput.Load(int3(np, 0)) * w;
            spatialW += w;
        }
    }
    float4 current = spatialSum / max(spatialW, 0.0001);

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
                // Map prevUV into atlas coordinates matching this pixel's half.
                float2 prevAtlasUV = isSpecular
                    ? float2(prevUV.x * 0.5 + 0.5, prevUV.y)
                    : float2(prevUV.x * 0.5, prevUV.y);
                int2 histPixel = int2(prevAtlasUV * float2(atlasRes));
                histPixel = clamp(histPixel, int2(0, 0), int2((int)atlasRes.x - 1, (int)atlasRes.y - 1));
                float4 history = _historyInput.Load(int3(histPixel, 0));

                // Radiance- and visibility-based clipping: if current and
                // history differ significantly, reduce hysteresis to avoid
                // lighting or occlusion ghosting.
                float curLum = dot(current.rgb, float3(0.299, 0.587, 0.114));
                float histLum = dot(history.rgb, float3(0.299, 0.587, 0.114));
                float lumDiff = abs(curLum - histLum) / max(max(curLum, histLum), 0.001);
                float visibilityDiff = abs(current.a - history.a);
                float change = max(saturate(lumDiff * 3.0), saturate(visibilityDiff * 4.0));
                float blendRate = lerp(1.0 - constants.params.x, 1.0, change);

                result = lerp(history, current, blendRate);
            }
        }
    }

    _indirectGI[pixel] = result;
    _historyOut[pixel] = result;
}
