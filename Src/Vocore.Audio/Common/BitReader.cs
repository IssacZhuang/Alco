namespace Vocore.Audio;

internal unsafe ref struct BitReader
{
    private readonly byte* _storedBuffer;
    private byte* _p;

    private int _bitOffset;
    private uint _cache;
    private int _position;

    public BitReader(byte* buffer)
    {
        _p = _storedBuffer = buffer;
        _bitOffset = 0;
        _position = 0;
        _cache = PeekCache();
    }


    public uint ReadBitsToUint(int bits)
    {
        if (bits <= 0 || bits > 32)
            throw new ArgumentOutOfRangeException(nameof(bits), "bits has to be a value between 1 and 32");

        uint result = _cache >> 32 - bits;
        if (bits <= 24)
        {
            SeekBits(bits);
            return result;
        }

        SeekBits(24);
        result |= _cache >> 56 - bits;
        SeekBits(bits - 24);

        return result;
    }

    public int ReadBitsToInt(int bits)
    {
        if (bits <= 0 || bits > 32)
            throw new ArgumentOutOfRangeException(nameof(bits), "bits has to be a value between 1 and 32");

        var result = (int)ReadBitsToUint(bits);
        result <<= (32 - bits);
        result >>= (32 - bits);
        return result;
    }

    public ulong ReadBitsToUlong(int bits)
    {
        if (bits <= 0 || bits > 64)
            throw new ArgumentOutOfRangeException(nameof(bits), "bits has to be a value between 1 and 64");

        ulong result = ReadBitsToUint(Math.Min(24, bits));
        if (bits <= 24)
            return result;

        bits -= 24;
        result = (result << bits) | ReadBitsToUint(Math.Min(24, bits));
        if (bits <= 24)
            return result;

        bits -= 24;
        return (result << bits) | ReadBitsToUint(bits);
    }

    public long ReadBits64ToLong(int bits)
    {
        if (bits <= 0 || bits > 64)
            throw new ArgumentOutOfRangeException(nameof(bits), "bits has to be a value between 1 and 64");

        var result = (long)ReadBitsToUlong(bits);
        result <<= (64 - bits);
        result >>= (64 - bits);
        return result;
    }

    private uint PeekCache()
    {
        unchecked
        {
            byte* ptr = _p;
            uint result = *(ptr++);
            result = (result << 8) + *(ptr++);
            result = (result << 8) + *(ptr++);
            result = (result << 8) + *(ptr++);

            return result << _bitOffset;
        }
    }

    private void SeekBits(int bits)
    {
        if (bits <= 0)
            throw new ArgumentOutOfRangeException("bits");

        int tmp = _bitOffset + bits;
        _p += tmp >> 3; //skip bytes
        _bitOffset = tmp & 7; //bitoverflow -> max 7 bit

        _cache = PeekCache();

        _position += tmp >> 3;
    }
}