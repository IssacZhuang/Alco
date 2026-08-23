// file "ssr-depth-downsample.slang" - module "ssr_depth_downsample"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/SsrDepthDownsample.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.
// Lib declarations are public so importing modules see them.

#language slang 2025
module ssr_depth_downsample;

import alco_rendering_core;
import alco_world3d_voxel_common;

// Transitional sampling shorthands: the preprocessor does not cross module
// boundaries, so the old Core.hlsli macros live here until Phase 3 deletes the
// name##Sampler convention outright.
#define SAMPLE_TEX2D(textureName, uv) textureName.Sample(textureName##Sampler, uv)
#define SAMPLE_TEX2D_LEVEL(textureName, uv, mip) textureName.SampleLevel(textureName##Sampler, uv, mip)
#define SAMPLE_TEX3D_LEVEL(textureName, uvw, mip) textureName.SampleLevel(textureName##Sampler, uvw, mip)
#define GET_PIXEL_TEX2D(textureName, position) textureName.Load(int3(position, 0))
#define LOAD_TEX3D(textureName, coord, mip) textureName.Load(int4(coord, mip))
#define SAMPLE_TEX2D_DEPTH_CMP(textureName, uv, compareDepth) textureName.SampleCmpLevelZero(textureName##Sampler, uv, compareDepth)

// Hi-Z depth pyramid construction for SSR hierarchical raymarching.
//
// Two modes controlled by the push constant:
//   isFirst = 1.0: copy full-resolution depth from the G-buffer depth buffer
//                  into pyramid mip 0 (dispatched at full G-buffer resolution).
//   isFirst = 0.0: 2x2 min-reduction from the previous mip (mipLevel) into
//                  mipLevel+1. NDC z is inverted (1.0 = near, 0.0 = far), so
//                  min keeps the nearest surface - conservative for raymarch
//                  intersection tests that skip large empty tiles.

struct SsrDepthConstants
{
    float4 params; // x = srcMipLevel, y = isFirst(1.0|0.0), zw = unused
};

// alco_world3d_voxel_common owns set 0 (_data); this pass's resources take set 1.
cbuffer _pass : register(b0, space1)
{
    DepthTexture2D _gbufferDepth;
    Texture2D _depthSrc;
    [[vk::image_format("rgba16f")]] RWTexture2D<float4> _depthOut;
};

[[vk::push_constant]] SsrDepthConstants constants;

[shader("compute")]
[numthreads(8, 8, 1)]
void MainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    int gbufferW = (int)giParams2.y;
    int gbufferH = (int)giParams2.z;
    uint srcMip = (uint)constants.params.x;
    bool isFirst = constants.params.y > 0.5;

    int dstW, dstH;
    if (isFirst)
    {
        dstW = gbufferW;
        dstH = gbufferH;
    }
    else
    {
        dstW = max(gbufferW >> (int)(srcMip + 1u), 1);
        dstH = max(gbufferH >> (int)(srcMip + 1u), 1);
    }

    if ((int)dispatchId.x >= dstW || (int)dispatchId.y >= dstH)
    {
        return;
    }

    int2 pixel = int2(dispatchId.xy);

    if (isFirst)
    {
        // Copy depth from the G-buffer depth attachment into mip 0.
        float depth = GetPixelDepth2D(_gbufferDepth, pixel);
        _depthOut[pixel] = float4(depth, 0.0, 0.0, 0.0);
        return;
    }

    // 2x2 min-reduction from the previous mip (rebased to 0 in the read view).
    int srcW = max(gbufferW >> (int)srcMip, 1);
    int srcH = max(gbufferH >> (int)srcMip, 1);
    int2 srcMax = int2(srcW - 1, srcH - 1);
    int2 p00 = pixel * 2;
    int2 p10 = int2(p00.x + 1, p00.y);
    int2 p01 = int2(p00.x, p00.y + 1);
    int2 p11 = int2(p00.x + 1, p00.y + 1);
    p00 = clamp(p00, int2(0, 0), srcMax);
    p10 = clamp(p10, int2(0, 0), srcMax);
    p01 = clamp(p01, int2(0, 0), srcMax);
    p11 = clamp(p11, int2(0, 0), srcMax);
    float d00 = _depthSrc.Load(int3(p00.x, p00.y, 0)).x;
    float d10 = _depthSrc.Load(int3(p10.x, p10.y, 0)).x;
    float d01 = _depthSrc.Load(int3(p01.x, p01.y, 0)).x;
    float d11 = _depthSrc.Load(int3(p11.x, p11.y, 0)).x;

    _depthOut[pixel] = float4(min(min(d00, d10), min(d01, d11)), 0.0, 0.0, 0.0);
}
