// file "volumetric-clouds-composite.slang" - module "volumetric_clouds_composite"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/VolumetricCloudsComposite.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module volumetric_clouds_composite;

import alco_rendering_core;
import alco_world3d_reversed_depth;
import alco_world3d_pbr_common;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// Volumetric clouds composite pass: a full-screen overlay on the HDR scene
// color that reconstructs the half-resolution cloud result (premultiplied
// scattering + opacity, written by VolumetricClouds.hlsl) with a 3x3
// gaussian-bilateral upsample and blends it over the completed scene with
// premultiplied-alpha blending (One, OneMinusSrcAlpha) — the scene color is
// never bound as a texture, the blend hardware performs the composite.
//
// The spatial half of the weights (a gaussian over the continuous half-res
// texel coordinate) averages the march pass's per-pixel jitter noise into a
// fine, isotropic blur — the spatial counterpart of the temporal averaging a
// TAA-based pipeline gets for free — while the depth-similarity half keeps
// cloud edges snapped to geometry silhouettes instead of haloing. Pure-sky
// regions (all taps at the far plane) degenerate to a plain gaussian upsample.

struct Vertex
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct V2F
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

// The half-resolution cloud march result.
[[vk::binding(0, 1)]] Texture2D _clouds;
[[vk::binding(1, 1)]] SamplerState _cloudsSampler;
// Shared G-buffer depth for the bilateral weights.
[[vk::binding(2, 1)]] DepthTexture2D _gbufferDepth;

// Squared gaussian width of the upsample kernel, in half-resolution texels
// (sigma ~ 0.67 texels): wide enough to average the march jitter grain into
// a fine isotropic blur, tight enough to keep cloud silhouettes crisp.
static const float CLOUD_UPSAMPLE_SIGMA2 = 0.45;


// The same _cloudData cbuffer the march pass binds (RGNode_VolumetricClouds
// fills it); only cloudParams2.z (march resolution scale) and cloudDebug are
// read here.
[[vk::binding(0, 2)]] cbuffer _cloudData {
    float4 cloudParams;  // x=coverage(0..1) y=density multiplier z=bottom altitude km w=slab thickness km
    float4 cloudParams2; // x=detailStrength y=extinction 1/km z=march resolution scale w=max march steps
    float4 cloudWind;    // xy=accumulated wind offset km z=time seconds w=detail drift phase
    float4 cloudLight;   // x=ambient strength y=sun strength z=aerial fade start km w=aerial fade end km
    float4 cloudDebug;   // x=opacity debug view (grayscale) yzw=unused
};


[shader("vertex")]
V2F MainVS(Vertex input)
{
    V2F output = (V2F)0;
    output.position = float4(input.position, 1.0f);
    output.uv = input.uv;
    return output;
}

// View-linear depth at a full-resolution uv (1e6 at the far plane — kept
// finite so the weight differences never turn into inf - inf = NaN).
float CloudCompositeLinearDepth(float2 uv)
{
    float depth = GetPixelDepth2D(_gbufferDepth, int2(uv * viewportSize.xy));
    if (IS_SKY_DEPTH(depth))
    {
        return 1e6;
    }
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float invW = dot(invViewProjection[3], float4(ndc, depth, 1.0));
    return abs(rcp(invW));
}

[shader("fragment")]
float4 MainPS(V2F input) : SV_TARGET
{
    float scale = cloudParams2.z;
    float2 halfSize = max(viewportSize.xy * scale, 1.0);
    float2 halfTexel = 1.0 / halfSize;

    // Continuous position of this full-resolution pixel in half-resolution
    // texel space (texel centers at n + 0.5), snapped to the nearest texel
    // center whose 3x3 neighborhood the gaussian taps below cover.
    float2 halfPos = input.position.xy * scale;
    float2 center = floor(halfPos) + 0.5;

    float centerDepth = CloudCompositeLinearDepth(input.uv);
    // Depth tolerance grows with distance: nearby geometry gets tight edges,
    // distant silhouettes still aggregate their sky taps.
    float sigma = max(centerDepth, 1.0) * 0.03;

    float3 scattering = 0.0;
    float opacity = 0.0;
    float weightSum = 0.0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 tapPos = clamp(center + float2(x, y), 0.5, halfSize - 0.5);
            float2 tapUv = tapPos * halfTexel;

            // Spatial gaussian over the sub-texel distance: substitutes the
            // old bilinear fractions while averaging the march jitter grain.
            float2 delta = halfPos - tapPos;
            float spatialWeight = exp(-0.5 * dot(delta, delta) / CLOUD_UPSAMPLE_SIGMA2);
            float tapDepth = CloudCompositeLinearDepth(tapUv);
            float depthWeight = exp(-abs(tapDepth - centerDepth) / sigma);

            float w = spatialWeight * depthWeight + 1e-5;
            float4 tap = SAMPLE_TEX2D(_clouds, tapUv);
            scattering += tap.rgb * w;
            opacity += tap.a * w;
            weightSum += w;
        }
    }

    scattering /= weightSum;
    opacity /= weightSum;

    if (cloudDebug.x > 0.5)
    {
        // Opacity debug view: replace the scene with the coverage mask.
        return float4(opacity.xxx, 1.0);
    }
    return float4(scattering, opacity);
}
