namespace Vocore.Audio;

internal unsafe struct FlacFrameHeader
{
    public static readonly uint[] SampleRateTable =
        [
            0, 88200, 176400, 192000,
            8000, 16000, 22050, 24000,
            32000, 44100, 48000, 96000,
            0, 0, 0, 0
        ];

    public static readonly uint[] BitPerSampleTable =
        [
            0, 8, 12, 0,
            16, 20, 24, 0
        ];

    public static readonly int[] FlacBlockSizes =
        [
            0, 192, 576, 1152,
            2304, 4608, 0, 0,
            256, 512, 1024, 2048,
            4096, 8192, 16384
        ];

    public uint SyncCode;//14 bits
    public uint Reserved;//1 bit
    public uint BlockingStrategy;//1 bit
    public int BlockSize;//4 bits
    public uint SampleRate;//4 bits
    public FlacChannelAssignment ChannelAssignment;//4 bits
    public uint Channels;// calculated from ChannelAssignment
    public uint BitsPerSample;//3 bits
    public int Reserved2;//1 bit
    public int FrameNumber;//8 bits
    public uint CRC8;//8 bits

    public FlacFrameHeader(byte* p, FlacMetadataStreamInfo streamInfo)
    {
        BitReader reader = new BitReader(p);
        SyncCode = reader.ReadBitsToUint(14);
        if (SyncCode != 0b11111111111110)
        {
            throw new Exception($"Invalid frame sync code: {SyncCode.ToString("B")}");
        }
        Reserved = reader.ReadBitsToUint(1);
        BlockingStrategy = reader.ReadBitsToUint(1);

        //Block size
        int val = p[2] >> 4;
        if (val == 0)
        {
            throw new Exception($"Invalid frame block size: {val}");
        }

        int uncommonBlockSizeCode = 0;

        if (val == 0b0001)
        {
            BlockSize = 192;
        }
        else if (val >= 0b0010 && val <= 0b0101)
        {
            BlockSize = 576 << (val - 2);
        }
        else if (val == 0b0110 && val == 0b0111)
        {
            uncommonBlockSizeCode = val;
        }
        else if (val >= 0b1000 && val <= 0b1111)
        {
            BlockSize = 256 << (val - 8);
        }
        else
        {
            throw new Exception($"Invalid frame block size: {val}");
        }

        int uncommonSampleRateCode = 0;

        //sample rate
        val = p[2] & 0x0F;
        if (val == 0b0000)
        {
            SampleRate = streamInfo.SampleRate;
        }
        else if (val >= 0b0001 && val <= 0b1011)
        {
            SampleRate = SampleRateTable[val];
        }
        else if (val == 0b1100 || val == 0b1110)
        {
            uncommonSampleRateCode = val;
        }
        else
        {
            throw new Exception($"Invalid frame sample rate: {val}");
        }

        //channel assignment
        val = p[3] >> 4;
        if ((val & 8) != 0)
        {
            Channels = 2;
            if ((val & 7) > 2 || (val & 7) < 0)
            {
                throw new Exception($"Invalid frame channel assignment: {val}");
            }
            ChannelAssignment = (FlacChannelAssignment)((val & 7) + 1);
        }
        else
        {
            Channels = (uint)(val + 1);
            ChannelAssignment = FlacChannelAssignment.Independent;
        }

        //bits per sample
        val = (p[3] & 0x0E) >> 1;
        if (val == 0)
        {
            BitsPerSample = streamInfo.BitsPerSample;
        }
        else if (val == 3 || val >= 7 || val < 0)
        {
            throw new Exception($"Invalid frame bits per sample: {val}");
        }
        else
        {
            BitsPerSample = BitPerSampleTable[val];
        }

        reader.ReadBitsToUint(32);//skip 

        

    }
}