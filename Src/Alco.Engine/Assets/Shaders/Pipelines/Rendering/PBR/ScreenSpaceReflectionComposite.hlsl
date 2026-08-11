#include "Shaders/Pipelines/Rendering/PBR/ScreenSpaceReflectionPostCommon.hlsli"

DEFINE_TEX2D_SAMPLE(1, _sceneColor);
DEFINE_TEX2D_SAMPLE(1, _reflection);
DEFINE_TEX2D_READ(1, _albedo);
DEFINE_TEX2D_READ(1, _normal);
DEFINE_TEX2D_READ(1, _mrAO);
DEFINE_TEX2D_DEPTH(1, _gbufferDepth);

[shader("pixel")]
float4 MainPS(V2F input) : SV_TARGET
{
    float4 scene = SAMPLE_TEX2D(_sceneColor, input.uv);
    float4 reflection = SAMPLE_TEX2D(_reflection, input.uv);

    // These two views are produced here instead of in deferred lighting so
    // the ray always samples the normal fully-lit scene color first.
    if (ssrParams.z > 1.5 && ssrParams.z < 2.5)
    {
        return float4(reflection.rgb * reflection.a, 1.0);
    }
    if (ssrParams.z > 4.5 && ssrParams.z < 5.5)
    {
        return float4(reflection.aaa, 1.0);
    }
    if (ssrParams.z > 0.5)
    {
        return scene;
    }

    float2 fullSize = ssrRenderSize.xy;
    int2 pixel = clamp(int2(input.uv * fullSize), int2(0, 0), int2(fullSize) - 1);
    float depth = GET_PIXEL_TEX2D(_gbufferDepth, pixel);
    if (depth >= 0.9999 || reflection.a <= 0.001)
    {
        return scene;
    }

    float4 packedAlbedo = GET_PIXEL_TEX2D(_albedo, pixel);
    float3 albedo = SsrPostDecodeSRGB(packedAlbedo.rgb);
    float roughness = packedAlbedo.a;
    float metallic = GET_PIXEL_TEX2D(_mrAO, pixel).x;
    float3 normal = normalize(GET_PIXEL_TEX2D(_normal, pixel).xyz * 2.0 - 1.0);
    float3 worldPosition = SsrPostReconstructWorldPosition(input.uv, depth);
    float3 V = normalize(ssrCameraPosition.xyz - worldPosition);
    float NdotV = saturate(dot(normal, V));
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 reflectionWeight = SsrPostEnvBRDF(F0, roughness, NdotV)
        * reflection.a * ssrParams.w;
    reflectionWeight *= saturate((ssrRayParams.y - roughness) / 0.25);

    // Complementary's reflection composite is a Fresnel-weighted replacement,
    // not an unbounded additive light. This preserves energy while allowing a
    // dark reflected object to remain dark.
    float3 color = lerp(scene.rgb, reflection.rgb, saturate(reflectionWeight));
    return float4(max(color, 0.0), scene.a);
}
