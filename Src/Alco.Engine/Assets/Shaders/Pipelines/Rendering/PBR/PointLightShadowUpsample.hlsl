#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/PointLightShadowCommon.hlsli"

// Point light shadows, stage 3 of 3 (full-resolution upsample).
// PointLightShadowResolve.hlsl accumulated the shadowed irradiance at the trace
// resolution; this pass expands it to the full G-buffer resolution with a
// depth-weighted 2x2 neighbourhood gather, so the deferred lighting pass can
// consume the result like any other full-res texture. Coplanar taps blend
// smoothly while taps across a depth discontinuity are rejected, keeping the
// shadow signal sharp at edges without blocking up at the trace resolution.
//
// Bind groups: set 0 packs the per-dispatch inputs together with the shared
// uniform (binding 0); set 1 is the full-resolution output.

// Trace-resolution resolved data (rgb = shadowed irradiance, a = receiver
// view-linear depth). Sampled with explicit LOD 0 so the compute shader does
// not require derivative capabilities.
DEFINE_TEX2D_SAMPLE(0, _plTrace);
DEFINE_TEX2D_DEPTH(0, _gbufferDepth);

DEFINE_TEX2D_STORAGE(1, _plOut, float4, "rgba16f");

float ReconstructLinearDepth(uint2 pixel)
{
    float2 uv = (float2(pixel) + 0.5) / float2(plParams2.x, plParams2.y);
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, int2(pixel));
    float reciprocalClipW = dot(invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchId.xy;
    uint2 viewportSize = uint2(plParams2.x, plParams2.y);
    if (pixel.x >= viewportSize.x || pixel.y >= viewportSize.y)
    {
        return;
    }

    // Sky pixels get black output — the lighting pass replaces them with sky.
    float rawDepth = GET_PIXEL_TEX2D(_gbufferDepth, int2(pixel));
    if (rawDepth >= 0.9999)
    {
        _plOut[pixel] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewportSize);
    float linearDepth = ReconstructLinearDepth(pixel);

    // 2x2 trace-texel neighbourhood around this pixel's trace-space position.
    // Each tap carries a bilinear position weight multiplied by a depth test
    // against the receiver depth the trace pixel stored in alpha.
    float2 traceTexel = 1.0 / float2(plParams.z, plParams.w);
    float2 tracePos = uv / traceTexel - 0.5;
    float2 fraction = frac(tracePos);
    int2 baseTexel = int2(floor(tracePos));

    float3 radianceSum = 0.0;
    float weightSum = 0.0;
    [unroll]
    for (int ty = 0; ty < 2; ty++)
    {
        [unroll]
        for (int tx = 0; tx < 2; tx++)
        {
            float2 tapUV = (float2(baseTexel + int2(tx, ty)) + 0.5) * traceTexel;
            if (any(tapUV < 0.0) || any(tapUV > 1.0))
            {
                continue;
            }

            float cornerWeight = (tx == 0 ? 1.0 - fraction.x : fraction.x)
                               * (ty == 0 ? 1.0 - fraction.y : fraction.y);
            if (cornerWeight <= 0.0)
            {
                continue;
            }

            float4 tap = _plTrace.SampleLevel(_plTraceSampler, tapUV, 0);
            // Alpha = the trace pixel's receiver view-linear depth; zero means
            // the trace pixel was sky — nothing to gather behind it.
            if (tap.a <= 0.0)
            {
                continue;
            }

            float depthTest = saturate((0.12 - abs(1.0 - linearDepth / tap.a)) * 4.0);
            // Small floor keeps coplanar taps valid so a single rejected
            // neighbour never pushes the pixel to the nearest-tap fallback.
            float tapWeight = cornerWeight * (depthTest + 0.001);

            radianceSum += tap.rgb * tapWeight;
            weightSum += tapWeight;
        }
    }

    // All taps rejected (strong depth edge): take the nearest tap rather than
    // averaging light across the edge.
    float3 radiance;
    if (weightSum < 0.0001)
    {
        int2 nearestTexel = baseTexel + int2(round(fraction));
        float2 nearestUV = (float2(nearestTexel) + 0.5) * traceTexel;
        nearestUV = clamp(nearestUV, traceTexel * 0.5, 1.0 - traceTexel * 0.5);
        radiance = _plTrace.SampleLevel(_plTraceSampler, nearestUV, 0).rgb;
    }
    else
    {
        radiance = radianceSum / weightSum;
    }

    _plOut[pixel] = float4(radiance, 1.0);
}
