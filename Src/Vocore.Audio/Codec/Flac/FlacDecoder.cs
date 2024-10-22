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

            FlacMetadataBlockHeader header = ReadMetadataBlockHeader(ref p);

            if (header.Type != FlacMetadataType.StreamInfo)
            {
                throw new Exception("StreamInfo not found in Flac file.");
            }

            FlacMetadataStreamInfo streamInfo = ReadMetadataStreamInfo(ref p);

            //skip other metadata blocks
            while (!header.IsLastMetadata)
            {
                header = ReadMetadataBlockHeader(ref p);
                //check pointer overflow
                if (p - ptr >= data.Length)
                {
                    throw new Exception("Invalid Flac file.");
                }

                p += header.Size;
            }

            // MemoryReader reader = new MemoryReader(ptr, (uint)data.Length);
            // FlacMetadataBlockHeader header = reader.Peek<FlacMetadataBlockHeader>();
            // reader.SkipBytes(FlacMetadataBlockHeader.ChunkSize);
            // if (header.Type != FlacMetadataType.StreamInfo)
            // {
            //     throw new Exception("StreamInfo not found in Flac file.");
            // }

            // FlacMetadataStreamInfo streamInfo = reader.Peek<FlacMetadataStreamInfo>();
            // reader.SkipBytes(FlacMetadataStreamInfo.ChunckSize);

            // //skip other metadata blocks
            // while (!header.IsLastMetadata)
            // {
            //     header = reader.Peek<FlacMetadataBlockHeader>();
            //     reader.SkipBytes(FlacMetadataBlockHeader.ChunkSize);
            // }


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

        // Skip the metadata block until we reach the audio data
    }

    private static FlacMetadataBlockHeader ReadMetadataBlockHeader(ref byte* p)
    {
        FlacMetadataBlockHeader header = new FlacMetadataBlockHeader(p);
        p += FlacMetadataBlockHeader.ChunkSize;
        return header;
    }

    private static FlacMetadataStreamInfo ReadMetadataStreamInfo(ref byte* p)
    {
        FlacMetadataStreamInfo streamInfo = new FlacMetadataStreamInfo(p);
        p += FlacMetadataStreamInfo.ChunckSize;
        return streamInfo;
    }
}