namespace Vocore.Audio;

public static unsafe class VorbisDecoder
{
    public static float[] DecodeWaveAudioToFloat32(ReadOnlySpan<byte> data, out int channels, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            // ----------- Header -----------

            byte* p = ptr;
            VorbisHeader header = *(VorbisHeader*)p;
            p += sizeof(VorbisHeader);

            byte* pSegmentLengths = p;
            p += header.PageSegments;


            // ----------- Metadata -----------

            byte packetType = *p;
            p += sizeof(byte);

            //skip comments
            uint vendorStringLength = *(uint*)p;
            p += sizeof(uint);
            p += vendorStringLength;

            uint commentsCount = *(uint*)p;
            p += sizeof(uint);

            for (int i = 0; i < commentsCount; i++)
            {
                uint commentLength = *(uint*)p;
                p += sizeof(uint);
                p += commentLength;
            }

            
        }

        throw new NotImplementedException();
    }
}