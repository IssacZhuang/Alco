// file "gaussian-blur-rgba32f.slang" - module "gaussian_blur_r_g_b_a32_f"
//
// Slang module converted from Shaders/Pipelines/Compute/GaussianBlurRGBA32F.hlsl (Phase 2 migration).
// DEFINE_* macros became explicit set/binding declarations; bindings within a
// set are assigned in the original declaration order and are compiler-owned.

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
cbuffer _input : register(b0, space0)
{
    [[vk::image_format("rgba32f")]] RWTexture2D<float4> _input;
};

cbuffer _output : register(b0, space1)
{
    [[vk::image_format("rgba32f")]] RWTexture2D<float4> _output;
};

// Gaussian kernel buffer (precomputed on CPU)
cbuffer _gaussianKernel : register(b0, space2)
{
    RWStructuredBuffer<float> _gaussianKernel;
};

[[vk::push_constant]] Constants constants;

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
    // Apply Gaussian blur and write the result to output texture
    float4 blurredColor = GaussianBlur(
        _input,
        _gaussianKernel, 
        constants.kernelSize, 
        constants.kernelSum, 
        id.xy, 
        constants.texSize
    );
    
    _output[id.xy] = blurredColor;
} 