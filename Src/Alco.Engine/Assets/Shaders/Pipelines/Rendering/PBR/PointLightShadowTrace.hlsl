#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/PointLightShadowCommon.hlsli"

// Point light shadows, stage 1 of 3 (half-resolution visibility trace). Every
// in-range point light's diffuse IRRADIANCE is evaluated per trace pixel
// (light color x intensity x attenuation x NdotL — no BRDF); lights with an
// atlas slot (from _plShadowInfo) are multiplied by their PCSS visibility
// sampled from the atlas, the rest stay unshadowed. The deferred lighting pass
// later divides this by its own full-resolution unshadowed irradiance to
// reconstruct a per-pixel visibility, which keeps NdotL terminators and GGX
// highlights sharp; storing the full radiance here instead would low-pass them
// at the trace resolution. Writes the raw (undenoised) irradiance (rgb) plus
// the receiver's view-linear depth (a); PointLightShadowResolve.hlsl accumulates
// it over frames and PointLightShadowUpsample.hlsl expands it to full
// resolution.
//
// Bind groups: set 0 packs the per-dispatch inputs together with the shared
// uniform (binding 0); set 1 is the trace-resolution raw output texture.

DEFINE_TEX2D_DEPTH(0, _gbufferDepth);
DEFINE_TEX2D_READ(0, _normal);
DEFINE_TEX2D_DEPTH_SAMPLE(0, _plShadowAtlas);
DEFINE_TEX2D_DEPTH(0, _plShadowAtlasLoad);

struct PointLightData
{
    float4 positionRange;    // xyz = world-space position, w = cutoff radius
    float4 colorIntensity;   // rgb = linear color, a = intensity (0 disables)
};
DEFINE_STORAGE(0, PointLightData, _pointLights);

#include "Shaders/Pipelines/Rendering/PBR/PointLightShadowSampling.hlsli"

DEFINE_TEX2D_STORAGE(1, _plRawOut, float4, "rgba16f");

// --- G-buffer reconstruction (same conventions as VoxelTrace.hlsl) ---

float3 ReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(invViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

float ReconstructLinearDepth(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float reciprocalClipW = dot(invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
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

    // Nearest G-buffer pixel for this trace pixel.
    float2 uv = (float2(tracePixel) + 0.5) / float2(traceResolution);
    uint2 gbufferResolution = uint2(plParams2.x, plParams2.y);
    int2 gbufferPixel = clamp((int2)(uv * float2(gbufferResolution)), int2(0, 0), (int2)gbufferResolution - 1);
    float2 gbufferUV = (float2(gbufferPixel) + 0.5) / float2(gbufferResolution);

    float depth = GET_PIXEL_TEX2D(_gbufferDepth, gbufferPixel);
    if (depth >= 0.9999)
    {
        _plRawOut[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    float linearDepth = ReconstructLinearDepth(gbufferUV, depth);

    float3 worldPosition = ReconstructWorldPosition(gbufferUV, depth);
    float3 N = normalize(GET_PIXEL_TEX2D(_normal, gbufferPixel).xyz * 2.0 - 1.0);

    float3 irradiance = 0.0;
    uint lightCount = (uint)plParams.x;
    [loop]
    for (uint i = 0; i < lightCount; i++)
    {
        float4 posRange = _pointLights[i].positionRange;
        float4 colInt = _pointLights[i].colorIntensity;
        if (colInt.w <= 0.0)
        {
            continue;
        }

        float3 toLight = posRange.xyz - worldPosition;
        float distance = length(toLight);
        if (posRange.w > 0.0 && distance > posRange.w)
        {
            continue;
        }

        float attenuation = 1.0 / (distance * distance + 1.0);
        if (posRange.w > 0.0)
        {
            float falloff = saturate(1.0 - distance / posRange.w);
            attenuation *= falloff * falloff;
        }

        float3 L = toLight / max(distance, 1e-6);
        float NdotL = saturate(dot(N, L));
        if (NdotL <= 0.0)
        {
            continue;
        }

        // Shadowed while the light has an atlas slot; the rest contribute
        // unshadowed so the total energy stays correct.
        float visibility = 1.0;
        float4 slotNearFar = _plShadowInfo[i].slotNearFar;
        if (slotNearFar.x >= 0.0)
        {
            visibility = SamplePointLightVisibility(
                worldPosition, N, L, posRange.xyz, distance,
                slotNearFar, plParams3, plParams.y, float2(tracePixel));
        }

        irradiance += colInt.rgb * colInt.w * attenuation * NdotL * visibility;
    }

    _plRawOut[tracePixel] = float4(irradiance, linearDepth);
}
