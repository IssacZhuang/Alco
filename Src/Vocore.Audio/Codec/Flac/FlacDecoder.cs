using System.Text;

namespace Vocore.Audio;

internal unsafe static class FlacDecoder
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("fLaC");

    public static float* DecodeWaveAudioToFloat32Unsafe(ReadOnlySpan<byte> data, out int channel, out int sampleCount, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            byte* p = ptr;

            CheckMagic(ref p);

            MemoryReader reader = new MemoryReader(p, (uint)data.Length);
            FlacMetadataBlockHeader header = new FlacMetadataBlockHeader(reader.CurrentPointer);
            reader.SkipBytes(FlacMetadataBlockHeader.ChunkSize);
            if (header.Type != FlacMetadataType.StreamInfo)
            {
                throw new Exception("StreamInfo not found in Flac file.");
            }

            FlacMetadataStreamInfo streamInfo = new FlacMetadataStreamInfo(reader.CurrentPointer);
            reader.SkipBytes(FlacMetadataStreamInfo.ChunckSize);

            //skip other metadata blocks
            while (!header.IsLastMetadata)
            {
                header = new FlacMetadataBlockHeader(reader.CurrentPointer);

                reader.SkipBytes(FlacMetadataBlockHeader.ChunkSize);
                reader.SkipBytes(header.Size);
            }

            FlacFrameHeader frameHeader = new FlacFrameHeader(reader.CurrentPointer, streamInfo);
        }
        throw new NotImplementedException();
    }


    private static void CheckMagic(ref byte* p)
    {
        for (int i = 0; i < 4; i++)
        {
            if (p[i] != Magic[i])
            {
                throw new Exception("Invalid FLAC header.");
            }
        }
        p += 4;
    }

}