// file "volumetric-cloud-shadow.slang" - module "volumetric_cloud_shadow"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VolumetricCloudShadow.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module volumetric_cloud_shadow;

import alco_rendering_core;
import alco_world3d_clouds;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// Cloud shadow coverage bake: a tiny 256x256 compute pass (dispatched by
// RGNode_VolumetricClouds every frame before the graph's lighting pass reads
// the previous result) that evaluates the vertical column transmittance
// through the cloud slab over a camera-centered world grid, and stores
// 1 - transmittance in R. The deferred lighting pass projects each receiver
// toward the sun onto the slab and dims the direct sun by this coverage, so
// cloud shadows drift across the terrain together with the visible clouds.
//
// The bake uses the base shape only (no detail erosion): shadow penumbrae
// come out slightly softer than the visible cloud edges, which reads as
// scattering softness rather than a mismatch.

cbuffer _cloudData : register(b0, space0) {
    float4 cloudParams;  // x=coverage(0..1) y=density multiplier z=bottom altitude km w=slab thickness km
    float4 cloudParams2; // x=detailStrength y=extinction 1/km z=march resolution scale w=max march steps
    float4 cloudWind;    // xy=accumulated wind offset km z=time seconds w=detail drift phase
    float4 cloudLight;   // x=ambient strength y=sun strength z=aerial fade start km w=aerial fade end km
    float4 cloudDebug;   // x=opacity debug view y=bake half extent km zw=bake window center (world xz, km)
};

cbuffer _cloudNoise : register(b0, space1)
{
    Texture3D _cloudBaseNoise;
    SamplerState _cloudBaseNoiseSampler;
};

cbuffer _shadowOut : register(b0, space2)
{
    [[vk::image_format("rgba8")]] RWTexture2D<float4> _shadowOut;
};

static const float CLOUD_SHADOW_BASE_SCALE = 1.0 / 24.0;
static const float CLOUD_SHADOW_COVERAGE_SCALE = 1.0 / 72.0;

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    float2 texel = (dispatchId.xy + 0.5) / 256.0;
    float2 worldXz = (texel - 0.5) * (2.0 * cloudDebug.y) + cloudDebug.zw;

    float bottomKm = cloudParams.z;
    float thicknessKm = cloudParams.w;
    float coverage = cloudParams.x;

    // Vertical column transmittance through three slab samples (Beer-Lambert).
    float od = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float3 posKm = float3(worldXz, bottomKm + thicknessKm * (0.25 + 0.25 * i));
        float3 winded = posKm + float3(cloudWind.xy, 0.0);
        float lowFreq = SAMPLE_TEX3D_LEVEL(_cloudBaseNoise, winded * CLOUD_SHADOW_COVERAGE_SCALE, 0).r;
        float4 baseNoise = SAMPLE_TEX3D_LEVEL(_cloudBaseNoise, winded * CLOUD_SHADOW_BASE_SCALE, 0);
        float h = saturate((posKm.z - bottomKm) / thicknessKm);
        float density = CloudBaseShape(baseNoise, CloudCoverageMask(lowFreq, coverage),
            CloudHeightGradient(h, coverage));
        od += density * (thicknessKm / 3.0);
    }
    od *= cloudParams2.y * cloudParams.y;

    float shadow = 1.0 - exp(-od);
    shadow = pow(shadow, 0.8); // soften full-opacity cores a touch
    _shadowOut[dispatchId.xy] = float4(shadow, 0.0, 0.0, 0.0);
}
