// file "volumetric-cloud-shadow.slang" - module "volumetric_cloud_shadow"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VolumetricCloudShadow.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module volumetric_cloud_shadow;

import alco_rendering_core;
import alco_world3d_clouds;

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

public struct CloudDataParams
{
    public float4 cloudParams;  // x=coverage(0..1) y=density multiplier z=bottom altitude km w=slab thickness km
    public float4 cloudParams2; // x=detailStrength y=extinction 1/km z=march resolution scale w=max march steps
    public float4 cloudWind;    // xy=accumulated wind offset km z=time seconds w=detail drift phase
    public float4 cloudLight;   // x=ambient strength y=sun strength z=aerial fade start km w=aerial fade end km
    public float4 cloudDebug;   // x=opacity debug view y=bake half extent km zw=bake window center (world xz, km)
};

public ParameterBlock<CloudDataParams> _cloudData;


public struct CloudNoiseParams
{
    public Texture3D _cloudBaseNoise;
    public SamplerState _cloudBaseNoiseSampler;
};

public ParameterBlock<CloudNoiseParams> _cloudNoise;


public struct ShadowOutParams
{
    public [[vk::image_format("rgba8")]] RWTexture2D<float4> _shadowOut;
};

public ParameterBlock<ShadowOutParams> _shadowOut;


static const float CLOUD_SHADOW_BASE_SCALE = 1.0 / 24.0;
static const float CLOUD_SHADOW_COVERAGE_SCALE = 1.0 / 72.0;

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    float2 texel = (dispatchId.xy + 0.5) / 256.0;
    float2 worldXz = (texel - 0.5) * (2.0 * _cloudData.cloudDebug.y) + _cloudData.cloudDebug.zw;

    float bottomKm = _cloudData.cloudParams.z;
    float thicknessKm = _cloudData.cloudParams.w;
    float coverage = _cloudData.cloudParams.x;

    // Vertical column transmittance through three slab samples (Beer-Lambert).
    float od = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float3 posKm = float3(worldXz, bottomKm + thicknessKm * (0.25 + 0.25 * i));
        float3 winded = posKm + float3(_cloudData.cloudWind.xy, 0.0);
        float lowFreq = _cloudNoise._cloudBaseNoise.SampleLevel(_cloudNoise._cloudBaseNoiseSampler, winded * CLOUD_SHADOW_COVERAGE_SCALE, 0).r;
        float4 baseNoise = _cloudNoise._cloudBaseNoise.SampleLevel(_cloudNoise._cloudBaseNoiseSampler, winded * CLOUD_SHADOW_BASE_SCALE, 0);
        float h = saturate((posKm.z - bottomKm) / thicknessKm);
        float density = CloudBaseShape(baseNoise, CloudCoverageMask(lowFreq, coverage),
            CloudHeightGradient(h, coverage));
        od += density * (thicknessKm / 3.0);
    }
    od *= _cloudData.cloudParams2.y * _cloudData.cloudParams.y;

    float shadow = 1.0 - exp(-od);
    shadow = pow(shadow, 0.8); // soften full-opacity cores a touch
    _shadowOut._shadowOut[dispatchId.xy] = float4(shadow, 0.0, 0.0, 0.0);
}
