#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/ReversedDepth.hlsli"

// Voxel GI full-resolution upsample pass. Runs after VoxelDemosaic as the last
// compute pass of the VoxelGI plugin: reads the 5x-trace-width indirect atlas
// and produces two full-GBuffer-resolution textures (_giDiffuseOut, _giSpecularOut)
// that the deferred lighting pass samples directly.
//
// The 5-tap depth-weighted cross-kernel upscaling, the near/far layer depth
// blend and the ALD (Average Light Direction) directional modulation are
// migrated here verbatim from the old DeferredLighting.hlsl inline GI path so
// the lighting shader can be a thin consumer.

DEFINE_UNIFORM(0, _data)
{
    float4x4 invViewProjection;
    float4 params; // xy = G-buffer size in pixels, z = 1/traceWidth (= 5/atlasWidth), w = 1/traceHeight
};

// Indirect atlas (5x trace width), output of VoxelDemosaic. Uses explicit
// LOD-0 sampling (SampleLevel) so the compute shader does not require
// derivative capabilities.
DEFINE_TEX2D_SAMPLE(1, _indirectGI);
// G-buffer depth (for linear-depth-weighted blending) and normal (for ALD).
DEFINE_TEX2D_DEPTH(2, _gbufferDepth);
DEFINE_TEX2D_READ(3, _normal);
// Full-resolution outputs consumed by DeferredLighting.
DEFINE_TEX2D_STORAGE(4, _giDiffuseOut, float4, "rgba16f");
DEFINE_TEX2D_STORAGE(5, _giSpecularOut, float4, "rgba16f");

#define SAMPLE_ATLAS(tex, uv) tex.SampleLevel(tex##Sampler, uv, 0)

float ReconstructLinearDepth(uint2 pixel)
{
    float2 uv = (float2(pixel) + 0.5) / params.xy;
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
    uint2 viewportSize = uint2(params.x, params.y);
    if (pixel.x >= viewportSize.x || pixel.y >= viewportSize.y)
    {
        return;
    }

    // Sky pixels get black output — the lighting pass replaces them with sky.
    float rawDepth = GET_PIXEL_TEX2D(_gbufferDepth, int2(pixel));
    if (IS_SKY_DEPTH(rawDepth))
    {
        _giDiffuseOut[pixel] = float4(0.0, 0.0, 0.0, 1.0);
        _giSpecularOut[pixel] = float4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewportSize);
    float3 N = normalize(GET_PIXEL_TEX2D(_normal, int2(pixel)).xyz * 2.0 - 1.0);

    // Atlas layout: 5 segments (diffuse near/far, specular, ALD near/far).
    const float segmentCount = 5.0;
    float2 traceUV = uv * float2(1.0 / segmentCount, 1.0);

    float linearDepth = ReconstructLinearDepth(pixel);
    float2 traceTexel = params.zw; // one trace texel in segment-local UV
    float2 atlasTexel = float2(traceTexel.x / segmentCount, traceTexel.y);
    float4 sampleTM = float4(atlasTexel * 1.5, atlasTexel * 0.25);
    const float2 sampleOffsets[5] =
    {
        float2( 0, -1) * sampleTM.xy - sampleTM.zw,
        float2( 0,  1) * sampleTM.xy - sampleTM.zw,
        float2(-1,  0) * sampleTM.xy - sampleTM.zw,
        float2( 1,  0) * sampleTM.xy - sampleTM.zw,
        float2( 0,  0) * sampleTM.xy - sampleTM.zw,
    };

    float3 indirectDiffuseSum = 0.0;
    float indirectDiffuseWeight = 0.0;
    float4 indirectAldSum = 0.0;
    float indirectAldWeight = 0.0;

    [unroll]
    for (int s = 0; s < 5; s++)
    {
        float2 tapUV = traceUV + sampleOffsets[s];
        float4 tapDiffuseMin = SAMPLE_ATLAS(_indirectGI, tapUV);
        float4 tapDiffuseMax = SAMPLE_ATLAS(_indirectGI, tapUV + float2(1.0 / segmentCount, 0.0));
        float4 tapAldMin = SAMPLE_ATLAS(_indirectGI, tapUV + float2(3.0 / segmentCount, 0.0));
        float4 tapAldMax = SAMPLE_ATLAS(_indirectGI, tapUV + float2(4.0 / segmentCount, 0.0));

        float tapDepthMin = max(4.0, tapDiffuseMin.a);
        float tapDepthMax = max(4.0, tapDiffuseMax.a);
        float tapLerp = saturate(
            (linearDepth - tapDepthMin) / max(tapDepthMax - tapDepthMin, 0.0001));
        float3 tapDiffuse = lerp(tapDiffuseMin.rgb, tapDiffuseMax.rgb, tapLerp);
        float4 tapAld = lerp(tapAldMin, tapAldMax, tapLerp);
        float tapDepth = lerp(tapDepthMin, tapDepthMax, tapLerp);

        float depthTest = saturate(
            (0.12 - abs(1.0 - linearDepth / tapDepth)) * 4.0);
        float tapWeight = depthTest * 0.25;
        if (s == 4)
        {
            tapWeight = saturate(tapWeight * 4.0);
        }
        tapWeight += 0.001;

        indirectDiffuseSum += tapDiffuse * tapWeight;
        indirectDiffuseWeight += tapWeight;
        indirectAldSum += tapAld * tapWeight;
        indirectAldWeight += tapWeight;
    }

    float4 indirectSpecularSection = SAMPLE_ATLAS(_indirectGI, traceUV + float2(2.0 / segmentCount, 0.0));
    float3 indirectDiffuse = indirectDiffuseSum / indirectDiffuseWeight;
    float3 indirectSpecular = indirectSpecularSection.rgb;
    float selectedVisibility = indirectSpecularSection.a;
    float4 indirectAld = indirectAldSum / max(indirectAldWeight, 0.0001);

    // ALD directional diffuse modulation — energy-conserving, centred at 1.0.
    float dirIntens = max(0.0, length(indirectAld.xyz));
    float aldBrightness = max(indirectAld.w, 0.0001);
    float dirFraction = saturate(dirIntens / aldBrightness);
    float3 aldDir = dirIntens > 0.0001 ? normalize(indirectAld.xyz) : N;
    float NdotAld = saturate(dot(N, aldDir));
    float directionalMod = lerp(1.0, NdotAld * 2.0, dirFraction);

    float3 diffuseOut = indirectDiffuse * directionalMod;

    _giDiffuseOut[pixel] = float4(diffuseOut, selectedVisibility);
    _giSpecularOut[pixel] = float4(indirectSpecular, 1.0);
}
