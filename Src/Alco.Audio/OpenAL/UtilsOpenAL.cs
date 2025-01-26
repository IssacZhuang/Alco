using Silk.NET.OpenAL;
using Silk.NET.OpenAL.Extensions.EXT;

namespace Alco.Audio.OpenAL;

internal class UtilsOpenAL
{
    public static BufferFormat GetBufferFormat(int channel)
    {
        switch (channel)
        {
            case 1:
                return (BufferFormat)FloatBufferFormat.Mono;
            case 2:
                return (BufferFormat)FloatBufferFormat.Stereo;
            default:
                throw new NotSupportedException($"{channel} channel audio not supported");
        }
    }
}