// file "GaussianBlurRgba8.slang" - module "GaussianBlurRgba8"
//
// Slang module converted from Shaders/Pipelines/Compute/GaussianBlurRGBA8.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module GaussianBlurRgba8;

import AlcoRendering_Core;
import AlcoRendering_GaussianBlur;



struct Constants {
    int2 texSize;       // Texture dimensions
    int2 kernelSize;    // Size of the Gaussian kernel (must be odd numbers)
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
    // Apply Gaussian blur and write the result to output texture
    float4 blurredColor = gaussianBlur(
        input.input,
        gaussianKernel.gaussianKernel, 
        constants.kernelSize, 
        constants.kernelSum, 
        id.xy, 
        constants.texSize
    );
    
    output.output[id.xy] = blurredColor;
} 