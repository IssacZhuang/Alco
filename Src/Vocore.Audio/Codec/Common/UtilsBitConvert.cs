namespace Vocore.Audio;

internal struct Int24
{
    public byte Byte0;
    public byte Byte1;
    public byte Byte2;
}

internal class UtilsBitConvert
{
    public static int Int24ToInt32(Int24 value)
    {
        return value.Byte0 | (value.Byte1 << 8) | (value.Byte2 << 16);
    }
}