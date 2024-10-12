namespace Vocore.Audio;

public static unsafe class VorbisDecoder
{
    public static float[] DecodeVorbisAudioToFloat32(ReadOnlySpan<byte> data, out int channels, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            // ----------- Header -----------

            byte* p = ptr;
            OggPage header = *(OggPage*)p;
            p += sizeof(OggPage);

            byte* pSegmentLengths = p;
            p += header.PageSegments;


            

            
        }

        throw new NotImplementedException();
    }
}