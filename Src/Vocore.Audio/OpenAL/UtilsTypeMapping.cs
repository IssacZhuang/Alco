using Silk.NET.OpenAL;

namespace Vocore.Audio.OpenAL;

internal static partial class UitlsOpenAL
{
    public static BufferFormat BufferFormatToOpenAL(AudioBufferFormat format)
    {
        switch (format)
        {
            case AudioBufferFormat.Mono8:
                return BufferFormat.Mono8;
            case AudioBufferFormat.Mono16:
                return BufferFormat.Mono16;
            case AudioBufferFormat.Stereo8:
                return BufferFormat.Stereo8;
            case AudioBufferFormat.Stereo16:
                return BufferFormat.Stereo16;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }
    }
}