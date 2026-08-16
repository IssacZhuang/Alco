#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/PointLightShadowCommon.hlsli"

// Point light shadows, stage 2 of 3 (temporal resolve). PointLightShadowTrace
// wrote the raw shadowed irradiance at the trace resolution; this pass
// accumulates it over frames: the receiver is reprojected into the previous
// frame's accumulation with depth-validated bilinear taps, the gathered history
// is clamped to the current frame's 3x3 raw neighbourhood so moving lights and
// occluders leave no ghost trails, and the result is blended with a fixed
// new-frame weight. Alpha keeps the CURRENT receiver depth for the upsample
// pass and the next frame's reprojection validation.
//
// Bind groups: set 0 packs the per-dispatch inputs together with the shared
// uniform (binding 0); set 1 is the resolved output.

DEFINE_TEX2D_DEPTH(0, _gbufferDepth);
DEFINE_TEX2D_READ(0, _normal);
DEFINE_TEX2D_READ(0, _plRaw);
DEFINE_TEX2D_READ(0, _plHistory);

DEFINE_TEX2D_STORAGE(1, _plOut, float4, "rgba16f");

// Temporal accumulation blend (new-frame weight): ~8-frame window, matched to
// the PCSS dither convergence.
static const float PLS_TEMPORAL_BLEND = 0.125;

// --- G-buffer reconstruction (same conventions as VoxelTrace.hlsl) ---

float3 ReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

// Reproject this frame's receiver into the previous frame's screen and gather
// the temporally accumulated irradiance with depth-validated bilinear taps.
// Returns false when no valid history exists (disocclusion, off-screen or the
// first frame after a resize).
bool GatherHistory(
    float3 worldPosition,
    float3 N,
    float3 V,
    uint2 traceResolution,
    out float3 history)
{
    history = 0.0;
    float4 previousClip = mul(viewProjectionPrev, float4(worldPosition, 1.0));
    if (previousClip.w <= 0.0)
    {
        return false;
    }
    float2 previousNdc = previousClip.xy / previousClip.w;
    float2 previousUv = float2(previousNdc.x * 0.5 + 0.5, 0.5 - previousNdc.y * 0.5);
    if (any(previousUv < 0.0) || any(previousUv > 1.0))
    {
        return false;
    }

    float2 previousTexel = previousUv * float2(traceResolution) - 0.5;
    int2 previousBase = int2(floor(previousTexel));
    float2 previousFraction = frac(previousTexel);
    float expectedPreviousDepth = abs(previousClip.w);
    // Face-on receivers use a tight depth threshold; grazing surfaces need more
    // room because one trace pixel spans a larger view-depth interval.
    float viewFacing = abs(dot(V, N));
    float depthThreshold = lerp(0.08, 0.02, viewFacing);

    float3 historySum = 0.0;
    float historyWeight = 0.0;
    bool historyValid = false;
    [unroll]
    for (int y = 0; y < 2; y++)
    {
        [unroll]
        for (int x = 0; x < 2; x++)
        {
            int2 historyPixel = previousBase + int2(x, y);
            if (any(historyPixel < int2(0, 0)) || any(historyPixel >= (int2)traceResolution))
            {
                continue;
            }

            float bilinearWeight = (x != 0 ? previousFraction.x : 1.0 - previousFraction.x)
                                 * (y != 0 ? previousFraction.y : 1.0 - previousFraction.y);
            float4 historySample = _plHistory.Load(int3(historyPixel, 0));
            // Alpha zero = the previous trace pixel was sky.
            if (historySample.a <= 0.0)
            {
                continue;
            }

            float depthRatio = abs(expectedPreviousDepth / max(historySample.a, 0.0001) - 1.0);
            float depthWeight = 1.0 - smoothstep(depthThreshold * 0.5, depthThreshold, depthRatio);
            float tapWeight = bilinearWeight * depthWeight;
            if (tapWeight <= 0.0)
            {
                continue;
            }

            historySum += historySample.rgb * tapWeight;
            historyWeight += tapWeight;
            historyValid = true;
        }
    }

    if (historyValid && historyWeight > 0.0001)
    {
        history = historySum / historyWeight;
        return true;
    }
    return false;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 tracePixel = dispatchId.xy;
    uint2 traceResolution = uint2(plParams.z, plParams.w);
    if (any(tracePixel >= traceResolution))
    {
        return;
    }

    float4 raw = _plRaw.Load(int3(tracePixel, 0));
    // Sky trace pixels (alpha zero) carry nothing to accumulate.
    if (raw.a <= 0.0)
    {
        _plOut[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    // First frame after (re)creation of the history textures: seed directly.
    if (plParams2.w <= 0.5)
    {
        _plOut[tracePixel] = raw;
        return;
    }

    // Nearest G-buffer pixel for this trace pixel (same mapping as the trace
    // pass) to reconstruct the receiver for reprojection.
    float2 uv = (float2(tracePixel) + 0.5) / float2(traceResolution);
    uint2 gbufferResolution = uint2(plParams2.x, plParams2.y);
    int2 gbufferPixel = clamp((int2)(uv * float2(gbufferResolution)), int2(0, 0), (int2)gbufferResolution - 1);
    float2 gbufferUV = (float2(gbufferPixel) + 0.5) / float2(gbufferResolution);

    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    float3 worldPosition = ReconstructWorldPosition(gbufferUV, depth);
    float3 N = normalize(GET_PIXEL_TEX2D(_normal, gbufferPixel).xyz * 2.0 - 1.0);
    float3 V = normalize(cameraPosition.xyz - worldPosition);

    // 3x3 raw neighbourhood bounds: the current frame is dithered but its
    // bounds contain the converged answer, so clamping the history to them
    // removes ghost trails of moved lights/occluders while letting static
    // penumbrae converge beyond a single frame's noise. Sky neighbours (alpha
    // zero) do not constrain the bounds.
    float3 neighborhoodMin = raw.rgb;
    float3 neighborhoodMax = raw.rgb;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
            {
                continue;
            }
            int2 neighborPixel = int2(tracePixel) + int2(x, y);
            if (any(neighborPixel < int2(0, 0)) || any(neighborPixel >= (int2)traceResolution))
            {
                continue;
            }
            float4 neighbor = _plRaw.Load(int3(neighborPixel, 0));
            if (neighbor.a <= 0.0)
            {
                continue;
            }
            neighborhoodMin = min(neighborhoodMin, neighbor.rgb);
            neighborhoodMax = max(neighborhoodMax, neighbor.rgb);
        }
    }
    // Small widening so the clamp does not pin the accumulation exactly to one
    // frame's dither extremes.
    float3 widen = (neighborhoodMax - neighborhoodMin) * 0.1 + 0.001;
    neighborhoodMin -= widen;
    neighborhoodMax += widen;

    float3 Lo = raw.rgb;
    float3 history = 0.0;
    if (GatherHistory(worldPosition, N, V, traceResolution, history))
    {
        history = clamp(history, neighborhoodMin, neighborhoodMax);
        Lo = lerp(history, Lo, PLS_TEMPORAL_BLEND);
    }

    _plOut[tracePixel] = float4(Lo, raw.a);
}
