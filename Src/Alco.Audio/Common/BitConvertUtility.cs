using System.Runtime.CompilerServices;

namespace Alco.Audio;

internal struct Int24
{
    public byte Byte0;
    public byte Byte1;
    public byte Byte2;

    public Int24(byte byte0, byte byte1, byte byte2)
    {
        Byte0 = byte0;
        Byte1 = byte1;
        Byte2 = byte2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int(Int24 value)
    {
        return BitConvertUtility.Int24ToInt32(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator uint(Int24 value)
    {
        return (uint)BitConvertUtility.Int24ToInt32(value);
    }

    public override string ToString()
    {
        return ((int)this).ToString();
    }
}

internal struct UInt24
{
    public byte Byte0;
    public byte Byte1;
    public byte Byte2;

    public UInt24(byte byte0, byte byte1, byte byte2)
    {
        Byte0 = byte0;
        Byte1 = byte1;
        Byte2 = byte2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator uint(UInt24 value)
    {
        return BitConvertUtility.UInt24ToUInt32(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int(UInt24 value)
    {
        return (int)BitConvertUtility.UInt24ToUInt32(value);
    }

    public override string ToString()
    {
        return ((uint)this).ToString();
    }
}

internal class BitConvertUtility
{
    public static int Int24ToInt32(Int24 value)
    {
        int temp = value.Byte0 | (value.Byte1 << 8) | (value.Byte2 << 16);
        if ((temp & 0x800000) != 0)
        {
            temp |= unchecked((int)0xff000000);
        }
        return temp;
    }

    public unsafe static uint UInt24ToUInt32(UInt24 value)
    {
        return value.Byte0 | (uint)(value.Byte1 << 8) | (uint)(value.Byte2 << 16);
    }
}