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
        //todo: implement compressed formats
        throw new NotImplementedException("Compressed formats are not supported yet");
    }

}