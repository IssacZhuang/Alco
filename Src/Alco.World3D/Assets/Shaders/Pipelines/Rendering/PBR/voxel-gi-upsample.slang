// file "voxel-gi-upsample.slang" - module "voxel_gi_upsample"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VoxelGiUpsample.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module voxel_gi_upsample;

import alco_rendering_core;
import alco_world3d_reversed_depth;

// Voxel GI full-resolution upsample pass. Runs after VoxelDemosaic as the last
// compute pass of the VoxelGI plugin: reads the 5x-trace-width indirect atlas
// and produces two full-GBuffer-resolution textures (_giDiffuseOut, _giSpecularOut)
// that the deferred lighting pass samples directly.
//
// The 5-tap depth-weighted cross-kernel upscaling, the near/far layer depth
// blend and the ALD (Average Light Direction) directional modulation are
// migrated here verbatim from the old DeferredLighting.hlsl inline GI path so
// the lighting shader can be a thin consumer.

public struct DataParams
{
    public float4x4 invViewProjection;
    public float4 params; // xy = G-buffer size in pixels, z = 1/traceWidth (= 5/atlasWidth), w = 1/traceHeight
};

public ParameterBlock<DataParams> _data;



// Indirect atlas (5x trace width), output of VoxelDemosaic. Uses explicit
// LOD-0 sampling (SampleLevel) so the compute shader does not require
// derivative capabilities.
// The pass block below follows the _data block above in declaration order.
public struct PassParams
{
    public Texture2D _indirectGI;
    public SamplerState _indirectGISampler;
    // G-buffer depth (for linear-depth-weighted blending) and normal (for ALD).
    public DepthTexture2D _gbufferDepth;
    public Texture2D _normal;
    // Full-resolution outputs consumed by DeferredLighting.
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> _giDiffuseOut;
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> _giSpecularOut;
};

public ParameterBlock<PassParams> _pass;


float ReconstructLinearDepth(uint2 pixel)
{
    float2 uv = (float2(pixel) + 0.5) / _data.params.xy;
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float depth = GetPixelDepth2D(_pass._gbufferDepth, int2(pixel));
    float reciprocalClipW = dot(_data.invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(reciprocalClipW));
}

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchId.xy;
    uint2 viewportSize = uint2(_data.params.x, _data.params.y);
    if (pixel.x >= viewportSize.x || pixel.y >= viewportSize.y)
    {
        return;
    }

    // Sky pixels get black output — the lighting pass replaces them with sky.
    float rawDepth = GetPixelDepth2D(_pass._gbufferDepth, int2(pixel));
    if (IS_SKY_DEPTH(rawDepth))
    {
        _pass._giDiffuseOut[pixel] = float4(0.0, 0.0, 0.0, 1.0);
        _pass._giSpecularOut[pixel] = float4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewportSize);
    float3 N = normalize(_pass._normal.Load(int3(int2(pixel), 0)).xyz * 2.0 - 1.0);

    // Atlas layout: 5 segments (diffuse near/far, specular, ALD near/far).
    const float segmentCount = 5.0;
    float2 traceUV = uv * float2(1.0 / segmentCount, 1.0);

    float linearDepth = ReconstructLinearDepth(pixel);
    float2 traceTexel = _data.params.zw; // one trace texel in segment-local UV
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
        float4 tapDiffuseMin = _pass._indirectGI.SampleLevel(_pass._indirectGISampler, tapUV, 0);
        float4 tapDiffuseMax = _pass._indirectGI.SampleLevel(_pass._indirectGISampler, tapUV + float2(1.0 / segmentCount, 0.0), 0);
        float4 tapAldMin = _pass._indirectGI.SampleLevel(_pass._indirectGISampler, tapUV + float2(3.0 / segmentCount, 0.0), 0);
        float4 tapAldMax = _pass._indirectGI.SampleLevel(_pass._indirectGISampler, tapUV + float2(4.0 / segmentCount, 0.0), 0);

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

    float4 indirectSpecularSection = _pass._indirectGI.SampleLevel(_pass._indirectGISampler, traceUV + float2(2.0 / segmentCount, 0.0), 0);
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

    _pass._giDiffuseOut[pixel] = float4(diffuseOut, selectedVisibility);
    _pass._giSpecularOut[pixel] = float4(indirectSpecular, 1.0);
}
