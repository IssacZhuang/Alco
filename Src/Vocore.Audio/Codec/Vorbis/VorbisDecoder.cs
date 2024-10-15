using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vocore.Audio;

public static unsafe class VorbisDecoder
{
    public static float[] DecodeVorbisAudioToFloat32(ReadOnlySpan<byte> data, out int channels, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            byte* p = ptr;


            uint totlaSegmentSize = GetTotalSegmentSize(p, out uint packetCount);

            uint* packetSizes = stackalloc uint[(int)packetCount];
            int indexPacketSizes = 0;

            byte* vorbisData = (byte*)Allocator.Alloc((int)totlaSegmentSize);
            byte* pVorbisData = vorbisData;


            OggPage page;
            do
            {
                page = *(OggPage*)p;
                p += sizeof(OggPage);

                byte* pSegmentTable = p;
                p += page.PageSegments;


                uint segmentSize = 0;
                byte size = 0;

                for (int i = 0; i < page.PageSegments; i++)
                {
                    size = pSegmentTable[i];

                    packetSizes[indexPacketSizes] += size;
                    segmentSize += size;

                    if (size < 255)
                    {
                        Console.WriteLine($"packetSizes[{indexPacketSizes}] = {packetSizes[indexPacketSizes]}");
                        indexPacketSizes++;
                    }
                }

                Unsafe.CopyBlock(pVorbisData, p, segmentSize);
                pVorbisData += segmentSize;

                p += segmentSize;

                //todo: continution page check


            } while ((page.PageFlag & OggPageFlag.End) != OggPageFlag.End);

            //reset pointer
            pVorbisData = vorbisData;
            indexPacketSizes = 0;

            //The identification header
            ValideateVorbisHeader(pVorbisData, VorbisHeaderType.Identification);
            VorbisIdentificationHeader identificationHeader = *(VorbisIdentificationHeader*)pVorbisData;
            pVorbisData += packetSizes[indexPacketSizes++];

            //The comment header, skipped
            //VorbisHeaderType type = *(VorbisHeaderType*)pVorbisData;
            ValideateVorbisHeader(pVorbisData, VorbisHeaderType.Comment);
            pVorbisData += packetSizes[indexPacketSizes++];

            //The setup header
            ValideateVorbisHeader(pVorbisData, VorbisHeaderType.Setup);
            VorbisSetupHeader setupHeader = *(VorbisSetupHeader*)pVorbisData;

            byte* pCodebook = pVorbisData + sizeof(VorbisSetupHeader);

            for (int i = 0; i < setupHeader.CodebookCount; i++)
            {
                VorbisCodebookData codebookData = *(VorbisCodebookData*)pCodebook;
                if (!UtilsVorbis.IsCodebook(pCodebook))
                {
                    throw new Exception("Invalid Vorbis codebook");
                }

                Console.WriteLine(codebookData.ToString());
            }

            pVorbisData += packetSizes[indexPacketSizes++];

        }

        throw new NotImplementedException();
    }

    //vorbis operations

    private static void ValideateVorbisHeader(byte* p, VorbisHeaderType type)
    {
        VorbisHeaderType headerType = *(VorbisHeaderType*)p;
        if (headerType != type)
        {
            throw new Exception($"Invalid Vorbis header type, expected {type} but got {headerType}");
        }

        if (!UtilsVorbis.IsVorbisHeader(p + 1))
        {
            throw new Exception("Invalid Vorbis header");
        }
    }

    private static void ReadPage(ref byte* p, out OggPage page, out uint segmentSize, out uint packetCount)
    {
        if (!UtilsVorbis.IsOggHeader(p))
        {
            throw new Exception("Invalid Ogg page header");
        }

        page = *(OggPage*)p;
        p += sizeof(OggPage);

        byte* pSegmentTable = p;
        p += page.PageSegments;

        uint packetSize = 0;

        segmentSize = 0;
        packetCount = 0;

        for (int i = 0; i < page.PageSegments; i++)
        {
            byte size = pSegmentTable[i];

            packetSize += size;
            segmentSize += size;
            p += size;

            if (segmentSize < 255)
            {
                packetCount++;
                segmentSize = 0;
            }
        }

        if (segmentSize > 0)
        {
            packetCount++;
        }
    }

    private static uint GetTotalSegmentSize(byte* p, out uint totalPacketCount)
    {
        uint totalSegmentSize = 0;
        totalPacketCount = 0;

        ReadPage(ref p, out OggPage page, out uint segmentSize, out uint packetCount);
        totalSegmentSize += segmentSize;
        totalPacketCount += packetCount;

        while ((page.PageFlag & OggPageFlag.End) != OggPageFlag.End)
        {
            ReadPage(ref p, out page, out segmentSize, out packetCount);
            totalSegmentSize += segmentSize;
            totalPacketCount += packetCount;
        }

        return totalSegmentSize;
    }
}