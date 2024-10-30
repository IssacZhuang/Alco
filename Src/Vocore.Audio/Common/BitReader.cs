using System.Runtime.CompilerServices;

namespace Vocore.Audio;

internal unsafe ref struct BitReader
{
    internal static readonly byte[] UnaryTable =
        {
            8, 7, 6, 6, 5, 5, 5, 5, 4, 4, 4, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        };

    private readonly byte* _storedBuffer;
    private byte* _p;

    private int _bitOffset;
    private uint _cache;
    private int _position;

    public readonly uint Cache
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _cache;
    }

    public readonly int Position => _position;

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

    public void SeekBits(int bits)
    {
        if (bits <= 0)
            throw new ArgumentOutOfRangeException("bits");

        int tmp = _bitOffset + bits;
        _p += tmp >> 3; //skip bytes
        _bitOffset = tmp & 7; //bitoverflow -> max 7 bit

        _cache = PeekCache();

        _position += tmp >> 3;
    }

    public bool ReadUTF8toULong(out ulong result)
    {
        uint x = ReadBitsToUint(8);
        ulong v;
        int i;

        if ((x & 0x80) == 0)
        {
            v = x;
            i = 0;
        }
        else if ((x & 0xC0) != 0 && (x & 0x20) == 0)
        {
            v = x & 0x1F;
            i = 1;
        }
        else if ((x & 0xE0) != 0 && (x & 0x10) == 0) /* 1110xxxx */
        {
            v = x & 0x0F;
            i = 2;
        }
        else if ((x & 0xF0) != 0 && (x & 0x08) == 0) /* 11110xxx */
        {
            v = x & 0x07;
            i = 3;
        }
        else if ((x & 0xF8) != 0 && (x & 0x04) == 0) /* 111110xx */
        {
            v = x & 0x03;
            i = 4;
        }
        else if ((x & 0xFC) != 0 && (x & 0x02) == 0) /* 1111110x */
        {
            v = x & 0x01;
            i = 5;
        }
        else if ((x & 0xFE) != 0 && (x & 0x01) == 0)
        {
            v = 0;
            i = 6;
        }
        else
        {
            result = ulong.MaxValue;
            return false;
        }

        for (; i != 0; i--)
        {
            x = ReadBitsToUint(8);
            if ((x & 0xC0) != 0x80)
            {
                result = ulong.MaxValue;
                return false;
            }

            v <<= 6;
            v |= (x & 0x3F);
        }

        result = v;
        return true;
    }

    public bool ReadUTF8ToUint(out uint result)
    {
        uint v, x;
        int i;

        x = ReadBitsToUint(8);
        if ((x & 0x80) == 0)
        {
            v = x;
            i = 0;
        }
        else if ((x & 0xC0) != 0 && (x & 0x20) == 0)
        {
            v = x & 0x1F;
            i = 1;
        }
        else if ((x & 0xE0) != 0 && (x & 0x10) == 0) /* 1110xxxx */
        {
            v = x & 0x0F;
            i = 2;
        }
        else if ((x & 0xF0) != 0 && (x & 0x08) == 0) /* 11110xxx */
        {
            v = x & 0x07;
            i = 3;
        }
        else if ((x & 0xF8) != 0 && (x & 0x04) == 0) /* 111110xx */
        {
            v = x & 0x03;
            i = 4;
        }
        else if ((x & 0xFC) != 0 && (x & 0x02) == 0) /* 1111110x */
        {
            v = x & 0x01;
            i = 5;
        }
        else
        {
            result = uint.MaxValue;
            return false;
        }

        for (; i != 0; i--)
        {
            x = ReadBitsToUint(8);
            if ((x & 0xC0) != 0x80)
            {
                result = uint.MaxValue;
                return false;
            }

            v <<= 6;
            v |= (x & 0x3F);
        }

        result = v;
        return true;
    }

    public uint ReadUnary()
    {
        uint result = 0;
        uint unaryindicator = _cache >> 24;

        while (unaryindicator == 0)
        {
            SeekBits(8);
            result += 8;
            unaryindicator = _cache >> 24;
        }

        result += UnaryTable[unaryindicator];
        SeekBits((int)(result & 7) + 1);
        return result;
    }

    public int ReadUnarySigned()
    {
        uint value = ReadUnary();
        return (int)(value >> 1 ^ -(int)(value & 1));
    }

    public void Flush()
    {
        if (_bitOffset > 0 && _bitOffset <= 8){
            SeekBits(8 - _bitOffset);
        }
    }
}