#ifndef PBR_STANDARD_HLSLI
#define PBR_STANDARD_HLSLI

#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/Surface.hlsli"

// The built-in glTF metallic-roughness surface: the four standard texture
// slots, each multiplied with its per-instance factor. Binding is by slot name
// (see StandardSurfaceSlots): a material texture slot "albedo" binds to
// _albedoTexture here, so the exact set numbers are an implementation detail
// of this file (sets 2-5 per the Surface.hlsli convention).

DEFINE_TEX2D_SAMPLE(2, _albedoTexture);
DEFINE_TEX2D_SAMPLE(3, _normalTexture);
DEFINE_TEX2D_SAMPLE(4, _metallicRoughnessTexture);
DEFINE_TEX2D_SAMPLE(5, _emissiveTexture);

// Identity vertex deformation: the standard surface does not animate vertices.
void ModifyVertex(inout float3 worldPos, inout float3 normalWS, float2 uv)
{
}

SurfaceOutput EvaluateSurface(SurfaceInput input)
{
    SurfaceOutput output = (SurfaceOutput)0;

#if defined(PASS_SHADOW)
    // Shadow depth only needs alpha; every other sample dead-code-eliminates
    // in this permutation (see the pass-define convention in Surface.hlsli).
    output.alpha = SAMPLE_TEX2D(_albedoTexture, input.uv).a * input.baseColorFactor.a;
    return output;
#else
#if defined(PASS_RSM)
    // The RSM only consumes albedo and the tangent-space normal; skip the
    // metallic-roughness and emissive samples in this permutation.
    float4 albedo = SAMPLE_TEX2D(_albedoTexture, input.uv);
    output.albedo = albedo.rgb * input.baseColorFactor.rgb;
    output.alpha = albedo.a * input.baseColorFactor.a;
    float2 normalXY = SAMPLE_TEX2D(_normalTexture, input.uv).rg * 2.0 - 1.0;
    output.normalTS = float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));
    return output;
#else
    float4 albedo = SAMPLE_TEX2D(_albedoTexture, input.uv);
    output.albedo = albedo.rgb * input.baseColorFactor.rgb;
    output.alpha = albedo.a * input.baseColorFactor.a;

    // glTF metallic-roughness texture: roughness in G, metallic in B, both
    // multiplied with their factors. AO stays factor-only.
    float4 mrTex = SAMPLE_TEX2D(_metallicRoughnessTexture, input.uv);
    output.roughness = input.metallicRoughnessAO.y * mrTex.g;
    output.metallic = input.metallicRoughnessAO.x * mrTex.b;
    output.ao = input.metallicRoughnessAO.z;

    // Two-channel tangent-space normal map (BC5); z is reconstructed.
    float2 normalXY = SAMPLE_TEX2D(_normalTexture, input.uv).rg * 2.0 - 1.0;
    output.normalTS = float3(normalXY, sqrt(saturate(1.0 - dot(normalXY, normalXY))));

    // Emissive texture (sRGB-decoded by the sampler) times the linear factor.
    output.emissive = SAMPLE_TEX2D(_emissiveTexture, input.uv).rgb * input.emissiveFactor.rgb;

    return output;
#endif
#endif
}

#endif // PBR_STANDARD_HLSLI
