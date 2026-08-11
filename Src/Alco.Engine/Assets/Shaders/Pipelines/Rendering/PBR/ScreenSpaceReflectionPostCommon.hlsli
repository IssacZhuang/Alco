#ifndef SCREEN_SPACE_REFLECTION_POST_COMMON_HLSLI
#define SCREEN_SPACE_REFLECTION_POST_COMMON_HLSLI

#include "Shaders/Libs/Core.hlsli"

// Shared by the post-lighting SSR trace, temporal resolve and composite passes.
// The layout must match ScreenSpaceReflectionRenderer.SsrData exactly.
DEFINE_UNIFORM(0, _ssrData)
{
    float4x4 ssrInvViewProjection;
    float4x4 ssrViewProjection;
    float4x4 ssrPreviousViewProjection;
    float4 ssrCameraPosition;
    float4 ssrRenderSize; // xy = full resolution, zw = trace resolution
    float4 ssrParams;     // x = frame index, y = history valid, z = debug mode, w = strength
    float4 ssrRayParams;  // x = max distance, y = roughness cutoff, zw = unused
};

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0);
    output.uv = input.uv;
    return output;
}

float3 SsrPostReconstructWorldPosition(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(ssrInvViewProjection, float4(ndc, depth, 1.0));
    return world.xyz / world.w;
}

bool SsrPostProjectWorldPosition(float3 worldPosition, out float3 screenPosition)
{
    float4 clip = mul(ssrViewProjection, float4(worldPosition, 1.0));
    if (clip.w <= 0.0)
    {
        screenPosition = 0.0;
        return false;
    }

    float3 ndc = clip.xyz / clip.w;
    screenPosition = float3(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5, ndc.z);
    return all(screenPosition.xy >= 0.0) && all(screenPosition.xy <= 1.0)
        && screenPosition.z >= 0.0 && screenPosition.z <= 1.0;
}

float SsrPostHash(float2 p)
{
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

float3 SsrPostDecodeSRGB(float3 color)
{
    float3 lo = color / 12.92;
    float3 hi = pow(max((color + 0.055) / 1.055, 0.0), 2.4);
    return lerp(hi, lo, step(color, float3(0.04045, 0.04045, 0.04045)));
}

// Lazarov split-sum BRDF approximation, matching DeferredLighting.hlsl.
float3 SsrPostEnvBRDF(float3 F0, float roughness, float NdotV)
{
    const float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    const float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    float2 AB = float2(-1.04, 1.04) * a004 + r.zw;
    return F0 * AB.x + AB.y;
}

#endif
