// file "screen-space-reflection-composite.slang" - module "screen_space_reflection_composite"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/ScreenSpaceReflectionComposite.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module screen_space_reflection_composite;

import alco_world3d_ssr_post_common;
import alco_world3d_reversed_depth;
import alco_rendering_core;

// alco_world3d_ssr_post_common declares _ssrData; this pass's ParameterBlock follows
// it in declaration order.
public struct PassParams
{
    public Texture2D _sceneColor;
    public SamplerState _sceneColorSampler;
    public Texture2D _reflection;
    public Texture2D _reflectionMetadata;
    public Texture2D _albedo;
    public Texture2D _normal;
    public Texture2D _mrAO;
    public DepthTexture2D _gbufferDepth;
};

public ParameterBlock<PassParams> _pass;


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
    int2 fullExtent = max(int2(_ssrData.ssrRenderSize.xy), int2(1, 1));
    int2 traceExtent = max(int2(_ssrData.ssrRenderSize.zw), int2(1, 1));
    int2 traceCenter = clamp(
        int2(uv * float2(traceExtent)), int2(0, 0), traceExtent - 1);

    // Full resolution has a one-to-one receiver mapping and needs no recovery
    // neighbourhood. An exact load also avoids a half-texel blur here.
    if (all(traceExtent == fullExtent))
    {
        return _pass._reflection.Load(int3(traceCenter, 0));
    }

    float receiverDistance = length(receiverWorld - _ssrData.ssrCameraPosition.xyz);
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

            float4 metadata = _pass._reflectionMetadata.Load(int3(candidate, 0));
            float4 reflection = _pass._reflection.Load(int3(candidate, 0));
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
            float candidateDepth = GetPixelDepth2D(_pass._gbufferDepth, candidateFullPixel);
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
                candidateWorld - _ssrData.ssrCameraPosition.xyz);
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
    float4 scene = _pass._sceneColor.Sample(_pass._sceneColorSampler, input.uv);

    // These two views are produced here instead of in deferred lighting so
    // the ray always samples the normal fully-lit scene color first.
    bool debugSpecular = _ssrData.ssrParams.z > 1.5 && _ssrData.ssrParams.z < 2.5;
    bool debugConfidence = _ssrData.ssrParams.z > 4.5 && _ssrData.ssrParams.z < 5.5;
    if (_ssrData.ssrParams.z > 0.5 && !debugSpecular && !debugConfidence)
    {
        return scene;
    }

    float2 fullSize = _ssrData.ssrRenderSize.xy;
    int2 pixel = clamp(int2(input.uv * fullSize), int2(0, 0), int2(fullSize) - 1);
    float depth = GetPixelDepth2D(_pass._gbufferDepth, pixel);
    if (IS_SKY_DEPTH(depth))
    {
        if (debugSpecular || debugConfidence)
        {
            return float4(0.0, 0.0, 0.0, 1.0);
        }
        return scene;
    }

    float3 normal = normalize(_pass._normal.Load(int3(pixel, 0)).xyz * 2.0 - 1.0);
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

    float4 packedAlbedo = _pass._albedo.Load(int3(pixel, 0));
    float3 albedo = SsrPostDecodeSRGB(packedAlbedo.rgb);
    float roughness = packedAlbedo.a;
    float metallic = _pass._mrAO.Load(int3(pixel, 0)).x;
    float3 V = normalize(_ssrData.ssrCameraPosition.xyz - worldPosition);
    float NdotV = saturate(dot(normal, V));
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 reflectionWeight = SsrPostEnvBRDF(F0, roughness, NdotV)
        * reflection.a * _ssrData.ssrParams.w;
    reflectionWeight *= saturate((_ssrData.ssrRayParams.y - roughness) / 0.25);

    // Complementary's reflection composite is a Fresnel-weighted replacement,
    // not an unbounded additive light. This preserves energy while allowing a
    // dark reflected object to remain dark.
    float3 color = lerp(scene.rgb, reflection.rgb, saturate(reflectionWeight));
    return float4(max(color, 0.0), scene.a);
}
