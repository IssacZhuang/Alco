namespace Alco.Audio;

internal unsafe struct FlacMetadataBlockHeader
{
    public const int ChunkSize = 4;

    public bool IsLastMetadata;//1 bits
    public FlacMetadataType Type;//7 bits
    public uint Size;//24 bits

    public FlacMetadataBlockHeader(byte* p)
    {
        BitReader reader = new BitReader(p);
        IsLastMetadata = reader.ReadBitsToUint(1) == 1;
        Type = (FlacMetadataType)reader.ReadBitsToUint(7);
        Size = reader.ReadBitsToUint(24);
    }

    public override string ToString()
    {
        return $"IsLastMetadata: {IsLastMetadata}, Type: {Type}, Size: {Size}";
    }
}