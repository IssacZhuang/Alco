#include "Shaders/Libs/Core.hlsli"
#include "Shaders/Libs/GaussianBlur.hlsli"

struct Constants {
    int2 texSize;       // Texture dimensions
    int2 kernelSize;    // Size of the Gaussian kernel (must be odd numbers)
    float kernelSum;    // Precomputed sum of kernel weights
};

// Input and output textures (using rgba16f format)
DEFINE_TEX2D_STORAGE(0, _inputTexture, float4, "rgba16f");
DEFINE_TEX2D_STORAGE(1, _outputTexture, float4, "rgba16f");

// Gaussian kernel buffer (precomputed on CPU)
DEFINE_STORAGE(2, float, _gaussianKernel);

PUSH_CONSTANT Constants constants;

[shader("compute")]
[numthreads(16, 16, 1)]
void MainCS(uint3 id: SV_DispatchThreadID) {
    // Apply Gaussian blur and write the result to output texture
    float4 blurredColor = GaussianBlur(
        _inputTexture,
        _gaussianKernel, 
        constants.kernelSize, 
        constants.kernelSum, 
        id.xy, 
        constants.texSize
    );
    
    _outputTexture[id.xy] = blurredColor;
} 