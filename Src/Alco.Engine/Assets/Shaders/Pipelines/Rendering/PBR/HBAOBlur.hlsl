#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Pipelines/Rendering/PBR/HBAOCommon.hlsli"

// Bilateral blur for the raw HBAO+ output: removes the per-pixel rotation/jitter
// noise without smearing occlusion across depth or normal discontinuities.
// Weights combine a small spatial Gaussian with view-depth and normal similarity.

#define HBAO_BLUR_RADIUS 2
#define HBAO_BLUR_SPATIAL_SIGMA 1.2
#define HBAO_BLUR_DEPTH_SIGMA 0.02
#define HBAO_BLUR_NORMAL_POWER 16.0

DEFINE_TEX2D_READ(1, _aoInput);
DEFINE_TEX2D_DEPTH(2, _gbufferDepth);
DEFINE_TEX2D_READ(3, _normal);
DEFINE_TEX2D_STORAGE(4, _aoOutput, float4, "rgba16f");

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
    float centerDepth = GET_PIXEL_TEX2D(_gbufferDepth, int2(pixel));
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

            float tapDepth = GET_PIXEL_TEX2D(_gbufferDepth, tapPixel);
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
    _aoOutput[pixel] = float4(ao, ao, ao, 1.0);
}
