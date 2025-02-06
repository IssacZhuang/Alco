using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal static partial class UtilsWebGPU
{
    public static WGPUTextureDataLayout GetTextureDataLayout(PixelFormat pixelFormat, uint width, uint height)
    {
        //uncompresed formats
        if (UtilsPixelFormat.TryGetPixelSize(pixelFormat, out var pixelSize))
        { 
            return new WGPUTextureDataLayout
            {
                offset = 0,
                bytesPerRow = width * pixelSize,
                rowsPerImage = height
            };

        }

        //compressed formats
        if (UtilsPixelFormat.TryGetCompressedBlockSize(pixelFormat, out var blockSize))
        {
            // BC formats use 4x4 pixel blocks
            uint blocksPerRow = (width + 3) / 4;
            return new WGPUTextureDataLayout
            {
                offset = 0,
                bytesPerRow = blocksPerRow * blockSize,
                rowsPerImage = height/4
            };
        }


        throw new NotImplementedException($"Format {pixelFormat} is not supported yet");
    }

}