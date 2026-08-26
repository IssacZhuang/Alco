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
    public float  coverage;             // 0..1 cloud coverage threshold
    public float  densityMultiplier;    // density scale
    public float  bottomAltitudeKm;     // cloud slab bottom altitude in km
    public float  slabThicknessKm;      // cloud slab thickness in km
    public float  detailStrength;       // detail erosion strength
    public float  extinctionPerKm;      // extinction coefficient 1/km
    public float  marchResolutionScale; // march resolution relative to the viewport
    public int   maxMarchSteps;        // maximum ray-march step count
    public float  windOffsetX;          // accumulated wind offset in km
    public float  windOffsetY;
    public float  timeSeconds;          // accumulated time in seconds
    public float  detailDriftPhase;     // detail noise drift phase
    public float  ambientStrength;      // ambient light strength
    public float  sunStrength;          // sun light strength
    public float  aerialFadeStartKm;    // aerial perspective fade start in km
    public float  aerialFadeEndKm;      // aerial perspective fade end in km
    public bool   opacityDebugView;     // grayscale opacity debug view
    public float  shadowExtentKm;       // shadow bake half extent in km
    public float2 shadowCenterKm;       // shadow bake window center (world xz, km)
};

public ParameterBlock<CloudDataParams> cloudData;


public struct CloudNoiseParams
{
    public Texture3D cloudBaseNoise;
};

public ParameterBlock<CloudNoiseParams> cloudNoise;


public struct ShadowOutParams
{
    public [[vk::image_format("rgba8")]] RWTexture2D<float4> shadowOut;
};

public ParameterBlock<ShadowOutParams> shadowOut;


static const float CLOUD_SHADOW_BASE_SCALE = 1.0 / 24.0;
static const float CLOUD_SHADOW_COVERAGE_SCALE = 1.0 / 72.0;

[shader("compute")]
[numthreads(8, 8, 1)]
void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    float2 texel = (dispatchId.xy + 0.5) / 256.0;
    float2 worldXz = (texel - 0.5) * (2.0 * cloudData.shadowExtentKm) + cloudData.shadowCenterKm;

    float bottomKm = cloudData.bottomAltitudeKm;
    float thicknessKm = cloudData.slabThicknessKm;
    float coverage = cloudData.coverage;

    // Vertical column transmittance through three slab samples (Beer-Lambert).
    float od = 0.0;
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float3 posKm = float3(worldXz, bottomKm + thicknessKm * (0.25 + 0.25 * i));
        float3 winded = posKm + float3(float2(cloudData.windOffsetX, cloudData.windOffsetY), 0.0);
        float lowFreq = cloudNoise.cloudBaseNoise.SampleLevel(samplers.linearRepeat, winded * CLOUD_SHADOW_COVERAGE_SCALE, 0).r;
        float4 baseNoise = cloudNoise.cloudBaseNoise.SampleLevel(samplers.linearRepeat, winded * CLOUD_SHADOW_BASE_SCALE, 0);
        float h = saturate((posKm.z - bottomKm) / thicknessKm);
        float density = cloudBaseShape(baseNoise, cloudCoverageMask(lowFreq, coverage),
            cloudHeightGradient(h, coverage));
        od += density * (thicknessKm / 3.0);
    }
    od *= cloudData.extinctionPerKm * cloudData.densityMultiplier;

    float shadow = 1.0 - exp(-od);
    shadow = pow(shadow, 0.8); // soften full-opacity cores a touch
    shadowOut.shadowOut[dispatchId.xy] = float4(shadow, 0.0, 0.0, 0.0);
}
