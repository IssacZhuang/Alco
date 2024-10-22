namespace Vocore.Audio;

internal unsafe struct FlacMetadataStreamInfo
{
    public const int ChunckSize = 34;

    public uint MinBlockSize;//16 bits
    public uint MaxBlockSize;//16 bits
    public uint MinFrameSize;//24 bits
    public uint MaxFrameSize;//24 bits
    public uint SampleRate;//20 bits
    public uint Channels;//3 bits
    public uint BitsPerSample;//5 bits
    public ulong TotalSamples;//36 bits
    public fixed byte Md5Checksum[16];//128 bits

    public FlacMetadataStreamInfo(byte* p)
    {
        BitReader reader = new BitReader(p);
        MinBlockSize = reader.ReadBitsToUint(16);
        MaxBlockSize = reader.ReadBitsToUint(16);
        MinFrameSize = reader.ReadBitsToUint(24);
        MaxFrameSize = reader.ReadBitsToUint(24);
        SampleRate = reader.ReadBitsToUint(20);
        Channels = reader.ReadBitsToUint(3) + 1;
        BitsPerSample = reader.ReadBitsToUint(5) + 1;
        TotalSamples = reader.ReadBitsToUlong(36);
        p += 18;
        for (int i = 0; i < 16; i++)
        {
            Md5Checksum[i] = *p++;
        }
    }

    public override string ToString()
    {
        return $"MinBlockSize: {MinBlockSize}, MaxBlockSize: {MaxBlockSize}, MinFrameSize: {MinFrameSize}, MaxFrameSize: {MaxFrameSize}, SampleRate: {SampleRate}, Channels: {Channels}, BitsPerSample: {BitsPerSample}, TotalSamples: {TotalSamples}";
    }
}