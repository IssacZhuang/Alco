using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal static partial class WebGPUUtility
{
    public static WGPUTexelCopyBufferLayout GetTextureDataLayout(PixelFormat pixelFormat, uint width, uint height)
    {
        //uncompresed formats
        if (PixelFormatUtility.TryGetPixelSize(pixelFormat, out var pixelSize))
        { 
            return new WGPUTexelCopyBufferLayout
            {
                offset = 0,
                bytesPerRow = width * pixelSize,
                rowsPerImage = height
            };

        }

        //compressed formats
        if (PixelFormatUtility.TryGetCompressedBlockSize(pixelFormat, out var blockSize))
        {
            // BC formats use 4x4 pixel blocks; bytesPerRow covers one block row, while
            // rowsPerImage stays in texel rows and must be a multiple of the block height.
            uint blocksPerRow = (width + 3) / 4;
            uint blockRowsPerImage = (height + 3) / 4;
            return new WGPUTexelCopyBufferLayout
            {
                offset = 0,
                bytesPerRow = blocksPerRow * blockSize,
                rowsPerImage = blockRowsPerImage * 4
            };
        }


        throw new NotImplementedException($"Format {pixelFormat} is not supported yet");
    }

}