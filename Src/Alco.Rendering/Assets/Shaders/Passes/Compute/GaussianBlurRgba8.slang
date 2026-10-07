// file "GaussianBlurRgba8.slang" - module "GaussianBlurRgba8"
//
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module GaussianBlurRgba8;

import AlcoRendering_Core;
import AlcoRendering_GaussianBlur;



struct Constants {
    int2 texSize;       // Texture dimensions
    int2 kernelSize;    // Size of the Gaussian kernel (must be odd numbers)
    int2 rectOrigin;    // Origin of the computed region, in texels
    int2 rectSize;      // Size of the computed region, in texels
    float kernelSum;    // Precomputed sum of kernel weights
};

// Input and output textures
public struct InputParams
{
    public [[vk::image_format("rgba8")]] RWTexture2D<float4> input;
};

public ParameterBlock<InputParams> input;

public struct OutputParams
{
    public [[vk::image_format("rgba8")]] RWTexture2D<float4> output;
};

public ParameterBlock<OutputParams> output;

// Gaussian kernel buffer (precomputed on CPU)
public struct GaussianKernelParams
{
    public RWStructuredBuffer<float> gaussianKernel;
};

public ParameterBlock<GaussianKernelParams> gaussianKernel;

[[vk::push_constant]] Constants constants;

[shader("compute")]
[numthreads(16, 16, 1)]
void mainCS(uint3 id: SV_DispatchThreadID) {
    int2 pos = constants.rectOrigin + int2(id.xy);

    // Threads past the region edge (dispatch groups round up) must not write: texels there hold
    // deliberately preserved values outside the computed region.
    if (any(pos >= constants.rectOrigin + constants.rectSize)) {
        return;
    }

    // Apply Gaussian blur and write the result to output texture
    float4 blurredColor = gaussianBlur(
        input.input,
        gaussianKernel.gaussianKernel,
        constants.kernelSize,
        constants.kernelSum,
        uint2(pos),
        constants.texSize
    );

    output.output[uint2(pos)] = blurredColor;
}