// file "ScreenSpaceReflectionComposite.slang" - module "screen_space_reflection_composite"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/ScreenSpaceReflectionComposite.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module screen_space_reflection_composite;

import alco_world3d_ssr_post_common;
import alco_world3d_reversed_depth;
import alco_rendering_core;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

[[vk::binding(0, 1)]] Texture2D _sceneColor;
[[vk::binding(1, 1)]] SamplerState _sceneColorSampler;
[[vk::binding(2, 1)]] Texture2D _reflection;
[[vk::binding(3, 1)]] Texture2D _reflectionMetadata;
[[vk::binding(4, 1)]] Texture2D _albedo;
[[vk::binding(5, 1)]] Texture2D _normal;
[[vk::binding(6, 1)]] Texture2D _mrAO;
[[vk::binding(7, 1)]] [[vk::image_format("r32f")]] Texture2D<float> _gbufferDepth;

// Upsample the trace-resolution history without allowing hardware bilinear
// filtering to mix unrelated receivers across a foreground/background edge.
// The resolve pass stores the receiver normal and linear camera distance beside
// every reflection texel, so the full-resolution receiver can reject samples
// that came from a different surface.
float4 SsrPostGeometryAwareUpsample(
    float2 uv,
    float3 receiverNormal,
    float3 receiverWorld)
{
    int2 fullExtent = max(int2(ssrRenderSize.xy), int2(1, 1));
    int2 traceExtent = max(int2(ssrRenderSize.zw), int2(1, 1));
    int2 traceCenter = clamp(
        int2(uv * float2(traceExtent)), int2(0, 0), traceExtent - 1);

    // Full resolution has a one-to-one receiver mapping and needs no recovery
    // neighbourhood. An exact load also avoids a half-texel blur here.
    if (all(traceExtent == fullExtent))
    {
        return GET_PIXEL_TEX2D(_reflection, traceCenter);
    }

    float receiverDistance = length(receiverWorld - ssrCameraPosition.xyz);
    float planeScale = 0.025 + receiverDistance * 0.002;
    float2 requestedTracePosition = uv * float2(traceExtent);
    float3 radianceSum = 0.0;
    float confidenceSum = 0.0;
    float weightSum = 0.0;

    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            int2 candidate = traceCenter + int2(x, y);
            if (any(candidate < int2(0, 0)) || any(candidate >= traceExtent))
            {
                continue;
            }

            float4 metadata = GET_PIXEL_TEX2D(_reflectionMetadata, candidate);
            float4 reflection = GET_PIXEL_TEX2D(_reflection, candidate);
            if (metadata.w < 0.5 || reflection.a <= 0.001)
            {
                continue;
            }

            float3 candidateNormal = SsrPostDecodeNormal(metadata.xy);
            float normalSimilarity = dot(receiverNormal, candidateNormal);
            if (normalSimilarity < 0.96)
            {
                continue;
            }

            float2 candidateUV = (float2(candidate) + 0.5) / float2(traceExtent);
            int2 candidateFullPixel = clamp(
                int2(candidateUV * float2(fullExtent)),
                int2(0, 0), fullExtent - 1);
            float candidateDepth = GetPixelDepth2D(_gbufferDepth, candidateFullPixel);
            if (IS_SKY_DEPTH(candidateDepth))
            {
                continue;
            }

            float3 candidateWorld = SsrPostReconstructWorldPosition(
                candidateUV, candidateDepth);
            // metadata.z describes the candidate receiver, not the full-resolution
            // receiver being reconstructed. Comparing it to receiverDistance rejects
            // valid samples along a grazing plane, where radial distance changes by
            // metres between adjacent screen rows. Validate the metadata against its
            // own current G-buffer sample instead.
            float candidateDistance = length(
                candidateWorld - ssrCameraPosition.xyz);
            float metadataDistanceScale = 0.04 + candidateDistance * 0.003;
            float metadataDistanceError = abs(metadata.z - candidateDistance);
            if (metadataDistanceError > metadataDistanceScale * 2.0)
            {
                continue;
            }

            float planeDistance = abs(dot(
                candidateWorld - receiverWorld, receiverNormal));
            if (planeDistance > planeScale * 2.0)
            {
                continue;
            }

            float2 traceOffset = float2(candidate) + 0.5
                - requestedTracePosition;
            float spatialWeight = exp(-dot(traceOffset, traceOffset) * 1.25);
            float geometryWeight = pow(saturate(normalSimilarity), 24.0)
                * exp(-metadataDistanceError / metadataDistanceScale
                    - planeDistance / planeScale);
            float weight = spatialWeight * geometryWeight;

            radianceSum += reflection.rgb * reflection.a * weight;
            confidenceSum += reflection.a * weight;
            weightSum += weight;
        }
    }

    // A sub-pixel receiver may have no representation in the reduced trace.
    // Returning no SSR is preferable to borrowing a bright background sample.
    if (confidenceSum <= 1e-5 || weightSum <= 1e-5)
    {
        return 0.0;
    }

    return float4(
        radianceSum / confidenceSum,
        saturate(confidenceSum / weightSum));
}

[shader("vertex")]
V2F MainVS(Vertex input)
{
    return SsrPostFullQuadVS(input);
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    float4 scene = SAMPLE_TEX2D(_sceneColor, input.uv);

    // These two views are produced here instead of in deferred lighting so
    // the ray always samples the normal fully-lit scene color first.
    bool debugSpecular = ssrParams.z > 1.5 && ssrParams.z < 2.5;
    bool debugConfidence = ssrParams.z > 4.5 && ssrParams.z < 5.5;
    if (ssrParams.z > 0.5 && !debugSpecular && !debugConfidence)
    {
        return scene;
    }

    float2 fullSize = ssrRenderSize.xy;
    int2 pixel = clamp(int2(input.uv * fullSize), int2(0, 0), int2(fullSize) - 1);
    float depth = GetPixelDepth2D(_gbufferDepth, pixel);
    if (IS_SKY_DEPTH(depth))
    {
        if (debugSpecular || debugConfidence)
        {
            return float4(0.0, 0.0, 0.0, 1.0);
        }
        return scene;
    }

    float3 normal = normalize(GET_PIXEL_TEX2D(_normal, pixel).xyz * 2.0 - 1.0);
    float3 worldPosition = SsrPostReconstructWorldPosition(input.uv, depth);
    float4 reflection = SsrPostGeometryAwareUpsample(
        input.uv, normal, worldPosition);
    if (debugSpecular)
    {
        return float4(reflection.rgb * reflection.a, 1.0);
    }
    if (debugConfidence)
    {
        return float4(reflection.aaa, 1.0);
    }
    if (reflection.a <= 0.001)
    {
        return scene;
    }

    float4 packedAlbedo = GET_PIXEL_TEX2D(_albedo, pixel);
    float3 albedo = SsrPostDecodeSRGB(packedAlbedo.rgb);
    float roughness = packedAlbedo.a;
    float metallic = GET_PIXEL_TEX2D(_mrAO, pixel).x;
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
