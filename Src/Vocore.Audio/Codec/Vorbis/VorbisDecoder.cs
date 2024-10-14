namespace Vocore.Audio;

public static unsafe class VorbisDecoder
{
    public static float[] DecodeVorbisAudioToFloat32(ReadOnlySpan<byte> data, out int channels, out int sampleRate)
    {
        fixed (byte* ptr = data)
        {
            // ----------- Header -----------

            byte* p = ptr;
            ReadPage(ref p, out OggPage page);

            Console.WriteLine(page.ToString());
            while ((page.PageFlag & OggPageFlag.End) != OggPageFlag.End)
            {
                ReadPage(ref p, out page);
                Console.WriteLine(page.ToString());
            }

        }

        throw new NotImplementedException();
    }

    private static void ReadPage(ref byte* p, out OggPage page)
    {
        byte* pStart = p;
        if (!OggPage.IsOggHeader(p))
        {
            throw new Exception("Invalid Ogg page header");
        }

        page = *(OggPage*)p;
        p += sizeof(OggPage);

        byte* pSegmentTable = p;
        p += page.PageSegments;

        //skip segment
        for (int i = 0; i < page.PageSegments; i++)
        {
            p += pSegmentTable[i];
        }

    }
}