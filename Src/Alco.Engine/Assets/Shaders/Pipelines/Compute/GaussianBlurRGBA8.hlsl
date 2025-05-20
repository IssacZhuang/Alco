#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/GaussianBlur.hlsli"



struct Constants {
    int2 texSize;       // Texture dimensions
    int2 kernelSize;    // Size of the Gaussian kernel (must be odd numbers)
    float kernelSum;    // Precomputed sum of kernel weights
};

// Input and output textures
DEFINE_TEX2D_STORAGE(0, _input, float4, "rgba8");
DEFINE_TEX2D_STORAGE(1, _output, float4, "rgba8");

// Gaussian kernel buffer (precomputed on CPU)
DEFINE_STORAGE(2, float, _gaussianKernel);

PUSH_CONSTANT Constants constants;

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