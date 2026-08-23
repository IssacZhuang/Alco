// file "hbao-blur.slang" - module "hbao_blur"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/HBAOBlur.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module hbao_blur;

import alco_rendering_core;
import alco_world3d_hbao_common;
import alco_world3d_reversed_depth;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// Bilateral blur for the raw HBAO+ output: removes the per-pixel rotation/jitter
// noise without smearing occlusion across depth or normal discontinuities.
// Weights combine a small spatial Gaussian with view-depth and normal similarity.
// Writes the blurred AO (scaled by the strength parameter) to a standalone
// full-resolution texture that the deferred lighting pass samples via the
// _aoTexture binding.

#define HBAO_BLUR_RADIUS 2
#define HBAO_BLUR_SPATIAL_SIGMA 1.2
#define HBAO_BLUR_DEPTH_SIGMA 0.02
#define HBAO_BLUR_NORMAL_POWER 16.0

// alco_world3d_hbao_common owns set 0 (_data); this pass's resources take set 1.
cbuffer _pass : register(b0, space1)
{
    Texture2D _aoInput;
    DepthTexture2D _gbufferDepth;
    Texture2D _normal;
    [[vk::image_format("rgba16f")]] RWTexture2D<float4> _aoResult;
};

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchId.xy;
    uint2 viewportSize = uint2(params2.y, params2.z);
    if (pixel.x >= viewportSize.x || pixel.y >= viewportSize.y)
    {
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewportSize);
    float centerDepth = GetPixelDepth2D(_gbufferDepth, int2(pixel));
    float centerViewDepth = ViewDepth(ReconstructWorldPosition(uv, centerDepth));
    float3 centerNormal = GET_PIXEL_TEX2D(_normal, int2(pixel)).xyz * 2.0 - 1.0;

    float aoSum = 0.0;
    float weightSum = 0.0;
    [unroll]
    for (int dy = -HBAO_BLUR_RADIUS; dy <= HBAO_BLUR_RADIUS; dy++)
    {
        [unroll]
        for (int dx = -HBAO_BLUR_RADIUS; dx <= HBAO_BLUR_RADIUS; dx++)
        {
            int2 tapPixel = int2(pixel) + int2(dx, dy);
            if (any(tapPixel < 0) || any(tapPixel >= int2(viewportSize)))
            {
                continue;
            }

            float spatialWeight = exp(-float(dx * dx + dy * dy) / (2.0 * HBAO_BLUR_SPATIAL_SIGMA * HBAO_BLUR_SPATIAL_SIGMA));

            float tapDepth = GetPixelDepth2D(_gbufferDepth, tapPixel);
            float2 tapUV = (float2(tapPixel) + 0.5) / float2(viewportSize);
            float tapViewDepth = ViewDepth(ReconstructWorldPosition(tapUV, tapDepth));
            float depthDelta = (tapViewDepth - centerViewDepth) / max(HBAO_BLUR_DEPTH_SIGMA * centerViewDepth, 1e-4);
            float depthWeight = exp(-depthDelta * depthDelta);

            float3 tapNormal = GET_PIXEL_TEX2D(_normal, tapPixel).xyz * 2.0 - 1.0;
            float normalWeight = pow(saturate(dot(tapNormal, centerNormal)), HBAO_BLUR_NORMAL_POWER);

            float weight = spatialWeight * depthWeight * normalWeight;
            aoSum += GET_PIXEL_TEX2D(_aoInput, tapPixel).r * weight;
            weightSum += weight;
        }
    }

    float ao = aoSum / max(weightSum, 1e-5);

    // Write the blurred AO to the standalone result texture. The lighting pass
    // multiplies this with the material AO from the G-buffer. White (1.0) when
    // strength is zero → no screen-space occlusion.
    float result = lerp(1.0, ao, saturate(params3.x));
    _aoResult[pixel] = float4(result, result, result, 1.0);
}
