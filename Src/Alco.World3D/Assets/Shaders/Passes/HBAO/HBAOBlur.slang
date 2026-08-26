// file "HBAOBlur.slang" - module "HBAOBlur"
//
// Slang module converted from Shaders/Pipelines/Rendering/PBR/HBAOBlur.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.
// Lib declarations are public so importing modules see them.

#language slang 2025
module HBAOBlur;

import AlcoRendering_Core;
import AlcoWorld3D_HBAOCommon;
import AlcoWorld3D_ReversedDepth;

// Bilateral blur for the raw HBAO+ output: removes the per-pixel rotation/jitter
// noise without smearing occlusion across depth or normal discontinuities.
// Weights combine a small spatial Gaussian with view-depth and normal similarity.
// Writes the blurred AO (scaled by the strength parameter) to a standalone
// full-resolution texture that the deferred lighting pass samples via the
// aoTexture binding.

#define HBAO_BLUR_RADIUS 2
#define HBAO_BLUR_SPATIAL_SIGMA 1.2
#define HBAO_BLUR_DEPTH_SIGMA 0.02
#define HBAO_BLUR_NORMAL_POWER 16.0

// AlcoWorld3D_HBAOCommon declares data; this pass's ParameterBlock follows it
// in declaration order.
public struct PassParams
{
    public Texture2D aoInput;
    public DepthTexture2D gbufferDepth;
    public Texture2D normal;
    public [[vk::image_format("rgba16f")]] RWTexture2D<float4> aoResult;
};

public ParameterBlock<PassParams> pass;


[shader("compute")]
[numthreads(8, 8, 1)]
void mainCS(uint3 dispatchId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchId.xy;
    uint2 viewportSize = uint2(data.viewportWidth, data.viewportHeight);
    if (pixel.x >= viewportSize.x || pixel.y >= viewportSize.y)
    {
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(viewportSize);
    float centerDepth = getPixelDepth2D(pass.gbufferDepth, int2(pixel));
    float centerViewDepth = viewDepth(reconstructWorldPosition(uv, centerDepth));
    float3 centerNormal = pass.normal.Load(int3(int2(pixel), 0)).xyz * 2.0 - 1.0;

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

            float tapDepth = getPixelDepth2D(pass.gbufferDepth, tapPixel);
            float2 tapUV = (float2(tapPixel) + 0.5) / float2(viewportSize);
            float tapViewDepth = viewDepth(reconstructWorldPosition(tapUV, tapDepth));
            float depthDelta = (tapViewDepth - centerViewDepth) / max(HBAO_BLUR_DEPTH_SIGMA * centerViewDepth, 1e-4);
            float depthWeight = exp(-depthDelta * depthDelta);

            float3 tapNormal = pass.normal.Load(int3(tapPixel, 0)).xyz * 2.0 - 1.0;
            float normalWeight = pow(saturate(dot(tapNormal, centerNormal)), HBAO_BLUR_NORMAL_POWER);

            float weight = spatialWeight * depthWeight * normalWeight;
            aoSum += pass.aoInput.Load(int3(tapPixel, 0)).r * weight;
            weightSum += weight;
        }
    }

    float ao = aoSum / max(weightSum, 1e-5);

    // Write the blurred AO to the standalone result texture. The lighting pass
    // multiplies this with the material AO from the G-buffer. White (1.0) when
    // strength is zero → no screen-space occlusion.
    float result = lerp(1.0, ao, saturate(data.strength));
    pass.aoResult[pixel] = float4(result, result, result, 1.0);
}
