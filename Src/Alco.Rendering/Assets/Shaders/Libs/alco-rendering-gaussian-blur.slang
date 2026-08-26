// file "alco-rendering-gaussian-blur.slang" - module "alco-rendering-gaussian-blur"
//
// Slang port of Shaders/Libs/gaussianBlur.hlsli: separable gaussian blur over
// an RW texture with weights from a kernel storage buffer.

#language slang 2025
module alco_rendering_gaussian_blur;

// Helper function to get 1D index from 2D kernel position
public uint getGaussianKernelIndex(int x, int y, int2 kernelSize)
{
    int halfKernelX = kernelSize.x / 2;
    int halfKernelY = kernelSize.y / 2;
    int row = y + halfKernelY;
    int col = x + halfKernelX;
    return row * kernelSize.x + col;
}

// Apply Gaussian blur to a given pixel position
// inputTexture: The source texture to blur
// kernelData: The Gaussian kernel weights
// kernelSize: The size of the kernel (x, y) - must be odd numbers
// kernelSum: The sum of all kernel weights for normalization
// pixelPos: The position of the pixel to blur
// texSize: The dimensions of the texture
// Returns: The blurred color value
public float4 gaussianBlur(RWTexture2D<float4> inputTexture,
                           RWStructuredBuffer<float> kernelData, int2 kernelSize,
                           float kernelSum, int2 pixelPos, int2 texSize)
{
    // Skip pixels outside texture bounds
    if (pixelPos.x >= texSize.x || pixelPos.y >= texSize.y)
    {
        return float4(0, 0, 0, 0);
    }

    int2 halfKernel = int2(kernelSize.x / 2, kernelSize.y / 2);
    float4 blurredColor = float4(0, 0, 0, 0);

    // Apply Gaussian blur
    for (int y = -halfKernel.y; y <= halfKernel.y; y++)
    {
        for (int x = -halfKernel.x; x <= halfKernel.x; x++)
        {
            // Calculate sample position with clamping to texture bounds
            int2 samplePos = int2(clamp(pixelPos.x + x, 0, texSize.x - 1),
                                  clamp(pixelPos.y + y, 0, texSize.y - 1));

            // Get kernel weight for this sample
            float kernelWeight = kernelData[getGaussianKernelIndex(x, y, kernelSize)];

            // Sample the texture and apply weight
            float4 sampleColor = inputTexture[samplePos];
            blurredColor += sampleColor * kernelWeight;
        }
    }

    // Normalize by kernel sum
    return blurredColor / kernelSum;
}
