#ifndef PBR_STANDARD_HLSLI
#define PBR_STANDARD_HLSLI

#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Surface.hlsli"

// The built-in glTF metallic-roughness surface: the four standard texture
// slots, each multiplied with its per-instance factor. Binding is by slot name
// (see StandardSurfaceSlotsUtility): a material texture slot "albedo" binds to
// _albedoTexture here. All slots share set 2 (the Surface.hlsli convention),
// so any subset of consumed functions keeps the bind group layout dense; no
// pass knowledge lives in this file.

DEFINE_TEX2D_SAMPLE(2, _albedoTexture);
DEFINE_TEX2D_SAMPLE(2, _normalTexture);
DEFINE_TEX2D_SAMPLE(2, _metallicRoughnessTexture);
DEFINE_TEX2D_SAMPLE(2, _emissiveTexture);

// Identity vertex deformation: the standard surface does not animate vertices.
void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv)
{
}

float4 GetBaseColor(SurfaceInput input)
{
    return SAMPLE_TEX2D(_albedoTexture, input.uv) * input.baseColorFactor;
}

float3 GetNormalTS(SurfaceInput input)
{
    // Two-channel tangent-space normal map (BC5); z is reconstructed.
    float2 normalXY = SAMPLE_TEX2D(_normalTexture, input.uv).rg * 2.0 - 1.0;
    return float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
}

float3 GetMetallicRoughnessAO(SurfaceInput input)
{
    // glTF metallic-roughness texture: roughness in G, metallic in B, both
    // multiplied with their factors. AO stays factor-only (no AO texture).
    float4 mrTex = SAMPLE_TEX2D(_metallicRoughnessTexture, input.uv);
    return float3(
        input.metallicRoughnessAO.x * mrTex.b,
        input.metallicRoughnessAO.y * mrTex.g,
        input.metallicRoughnessAO.z);
}

float3 GetEmissive(SurfaceInput input)
{
    // Emissive texture (sRGB-decoded by the sampler) times the linear factor.
    return SAMPLE_TEX2D(_emissiveTexture, input.uv).rgb * input.emissiveFactor.rgb;
}

#endif
