using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal static partial class UtilsWebGPU
{
    public static uint GetTextureBytesPerRow(PixelFormat pixelFormat, uint width, uint height)
    {
        //uncompresed formats
        if (UtilsPixelFormat.TryGetPixelSize(pixelFormat, out var pixelSize))
        { 
            return width * pixelSize;
        }

        //compressed formats
        if (UtilsPixelFormat.TryGetCompressedBlockSize(pixelFormat, out var blockSize))
        {
            // BC formats use 4x4 pixel blocks
            uint blocksPerRow = (width + 3) / 4;
            return blocksPerRow * blockSize;
        }

        throw new NotImplementedException($"Format {pixelFormat} is not supported yet");
    }

}