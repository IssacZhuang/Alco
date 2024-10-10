namespace Vocore.Audio;

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
}

internal class UtilsBitConvert
{
    public static int Int24ToInt32(Int24 value)
    {
        return value.Byte0 | (value.Byte1 << 8) | (value.Byte2 << 16);
    }
}