// file "gaussian-blur-rgba32f.slang" - module "gaussian_blur_r_g_b_a32_f"
//
// Slang module converted from Shaders/Pipelines/Compute/GaussianBlurRGBA32F.hlsl (Phase 2 migration).
// Resources are grouped as ParameterBlocks — the compiler assigns each block
// its own descriptor set in declaration order; members resolve by bare field name.

#language slang 2025
module gaussian_blur_rgba32f;

import alco_rendering_core;
import alco_rendering_gaussian_blur;

struct Constants {
    int2 texSize;       // Texture dimensions
    int2 kernelSize;    // Size of the Gaussian kernel (must be odd numbers)
    float kernelSum;    // Precomputed sum of kernel weights
};

// Input and output textures (using rgba32f format)
public struct InputParams
{
    public [[vk::image_format("rgba32f")]] RWTexture2D<float4> _input;
};

public ParameterBlock<InputParams> _input;

public struct OutputParams
{
    public [[vk::image_format("rgba32f")]] RWTexture2D<float4> _output;
};

public ParameterBlock<OutputParams> _output;

// Gaussian kernel buffer (precomputed on CPU)
public struct GaussianKernelParams
{
    public RWStructuredBuffer<float> _gaussianKernel;
};

public ParameterBlock<GaussianKernelParams> _gaussianKernel;

[[vk::push_constant]] Constants constants;

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
    // Apply Gaussian blur and write the result to output texture
    float4 blurredColor = GaussianBlur(
        _input._input,
        _gaussianKernel._gaussianKernel, 
        constants.kernelSize, 
        constants.kernelSum, 
        id.xy, 
        constants.texSize
    );
    
    _output._output[id.xy] = blurredColor;
} 