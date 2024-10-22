namespace Vocore.Audio;

internal unsafe struct FlacMetadataStreamInfo
{
    public const uint ChunckSize = 34;

    public uint MinBlockSize;//16 bits
    public uint MaxBlockSize;//16 bits
    public uint MinFrameSize;//24 bits
    public uint MaxFrameSize;//24 bits
    public uint SampleRate;//20 bits
    public uint Channels;//3 bits
    public uint BitsPerSample;//5 bits
    public ulong TotalSamples;//36 bits
    public fixed byte Md5Checksum[16];//128 bits

    public FlacMetadataStreamInfo(byte* ptr)
    {
        BitReader reader = new BitReader(ptr);
        MinBlockSize = reader.ReadBitToUint(16);
        MaxBlockSize = reader.ReadBitToUint(16);
        MinFrameSize = reader.ReadBitToUint(24);
        MaxFrameSize = reader.ReadBitToUint(24);
        SampleRate = reader.ReadBitToUint(20);
        Channels = reader.ReadBitToUint(3);
        BitsPerSample = reader.ReadBitToUint(5);
        TotalSamples = reader.ReadBitToUlong(36);
        ptr += 18;
        for (int i = 0; i < 16; i++)
        {
            Md5Checksum[i] = *ptr++;
        }
    }

    public override string ToString()
    {
        return $"MinBlockSize: {MinBlockSize}, MaxBlockSize: {MaxBlockSize}, MinFrameSize: {MinFrameSize}, MaxFrameSize: {MaxFrameSize}, SampleRate: {SampleRate}, Channels: {Channels}, BitsPerSample: {BitsPerSample}, TotalSamples: {TotalSamples}";
    }
}