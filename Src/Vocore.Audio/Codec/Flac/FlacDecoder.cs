using System.Text;

namespace Vocore.Audio;

internal unsafe static class FlacDecoder
{
    

    public static float* DecodeWaveAudioToFloat32Unsafe(ReadOnlySpan<byte> data, out int channel, out int sampleCount, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
           

            
        }
        throw new NotImplementedException();
    }


    


}