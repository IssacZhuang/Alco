namespace Vocore.Audio;

internal unsafe ref struct BitReader
{
    private readonly byte* _buffer;
    private byte* p;
    private byte _bitOffset;

    public byte BitOffset => _bitOffset;

    public BitReader(byte* buffer)
    {
        _buffer = buffer;

        p = buffer;
        _bitOffset = 0;
    }

    public bool ReadBitBool()
    {
        return ReadBit(1) == 1u;
    }

    public uint ReadBit(byte bits)
    {
        if (bits == 0)
        {
            return 0;
        }

        if (bits > 32 || bits < 0)
        {
            throw new ArgumentException("Bits must be between 0 and 32.");
        }
        uint data = *(uint*)p;
        data >>= _bitOffset;
        data &= (1u << bits) - 1u;

        _bitOffset += bits;
        p += _bitOffset / 8;
        _bitOffset %= 8;

        return data;
    }

}